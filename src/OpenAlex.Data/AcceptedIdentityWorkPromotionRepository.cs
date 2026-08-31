using Microsoft.Data.SqlClient;

namespace OpenAlex.Data;

public sealed class AcceptedIdentityWorkPromotionRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AcceptedIdentityWorkPromotionRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AcceptedIdentityWorkPromotionSafetyCounts> GetSafetyCountsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    WorkRows = (SELECT COUNT(*) FROM openalex.Work),
    PublicationCandidateRows =
        CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
             THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationCandidate) END,
    PublicationDecisionRows =
        CASE WHEN OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NULL
             THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyPublicationDecision) END;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Could not read work-promotion safety counts.");
        }

        return new AcceptedIdentityWorkPromotionSafetyCounts
        {
            WorkRows = reader.GetInt32(0),
            PublicationCandidateRows = reader.GetInt32(1),
            PublicationDecisionRows = reader.GetInt32(2)
        };
    }

    public async Task<long> StartRunAsync(
        string cohortName,
        int expectedFacultyCount,
        int expectedMissingWorkCount,
        int maxWorks,
        AcceptedIdentityWorkPromotionSafetyCounts safetyBefore,
        string startedBy,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureAuditTablesAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcceptedIdentityWorkPromotionRun
(
    CohortName,
    ExpectedFacultyCount,
    ExpectedMissingWorkCount,
    MaxWorks,
    ConfirmedPromotion,
    WorkRowsBefore,
    PublicationCandidateRowsBefore,
    PublicationDecisionRowsBefore,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    StartedAtUtc,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.AcceptedIdentityWorkPromotionRunId
VALUES
(
    @CohortName,
    @ExpectedFacultyCount,
    @ExpectedMissingWorkCount,
    @MaxWorks,
    1,
    @WorkRowsBefore,
    @PublicationCandidateRowsBefore,
    @PublicationDecisionRowsBefore,
    'Running',
    @StartedBy,
    @ApplicationVersion,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@CohortName", cohortName);
        command.Parameters.AddWithValue("@ExpectedFacultyCount", expectedFacultyCount);
        command.Parameters.AddWithValue("@ExpectedMissingWorkCount", expectedMissingWorkCount);
        command.Parameters.AddWithValue("@MaxWorks", maxWorks);
        command.Parameters.AddWithValue("@WorkRowsBefore", safetyBefore.WorkRows);
        command.Parameters.AddWithValue("@PublicationCandidateRowsBefore", safetyBefore.PublicationCandidateRows);
        command.Parameters.AddWithValue("@PublicationDecisionRowsBefore", safetyBefore.PublicationDecisionRows);
        command.Parameters.AddWithValue("@StartedBy", startedBy);
        command.Parameters.AddWithValue("@ApplicationVersion", applicationVersion);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task AttachIngestionRunAsync(
        long promotionRunId,
        long ingestionRunId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.AcceptedIdentityWorkPromotionRun
SET IngestionRunId = @IngestionRunId,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcceptedIdentityWorkPromotionRunId = @PromotionRunId;";
        command.Parameters.AddWithValue("@PromotionRunId", promotionRunId);
        command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AcceptedIdentityWorkPromotionSourceRow>> LoadSourceRowsAsync(
        string cohortName,
        int expectedFacultyCount,
        int expectedMissingWorkCount,
        int maxWorks,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SET NOCOUNT ON;

DECLARE @EnabledFacultyCount INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM openalex.PilotFacultyCohort
    WHERE CohortName = @CohortName
      AND IsEnabled = 1
);

IF @EnabledFacultyCount <> @ExpectedFacultyCount
BEGIN
    RAISERROR('Enabled cohort faculty count changed after preflight.', 16, 1);
    RETURN;
END;

;WITH AcceptedIdentity AS
(
    SELECT
        pc.PilotOrder,
        d.FacultyId,
        f.DisplayName AS FacultyDisplayName,
        d.AuthorOpenAlexId
    FROM openalex.PilotFacultyCohort pc
    INNER JOIN openalex.Faculty f
        ON f.FacultyId = pc.FacultyId
    INNER JOIN openalex.FacultyAuthorMatchDecision d
        ON d.FacultyId = pc.FacultyId
       AND d.DecisionStatus = 'Accepted'
       AND d.IsCurrent = 1
    WHERE pc.CohortName = @CohortName
      AND pc.IsEnabled = 1
),
Expanded AS
(
    SELECT
        ai.PilotOrder,
        ai.FacultyId,
        ai.FacultyDisplayName,
        ai.AuthorOpenAlexId,
        w.FacultyIdentityCandidateWorkId,
        w.WorkOpenAlexId,
        w.WorkDisplayName,
        w.WorkRawJson,
        ExistingNormalizedWorkId = nw.WorkId
    FROM AcceptedIdentity ai
    INNER JOIN openalex.FacultyIdentityCandidateWork w
        ON w.FacultyId = ai.FacultyId
       AND w.CandidateAuthorOpenAlexId = ai.AuthorOpenAlexId
    LEFT JOIN openalex.Work nw
        ON nw.OpenAlexId = w.WorkOpenAlexId
),
Missing AS
(
    SELECT *
    FROM Expanded
    WHERE ExistingNormalizedWorkId IS NULL
),
Ranked AS
(
    SELECT
        m.*,
        rn = ROW_NUMBER() OVER
        (
            PARTITION BY m.WorkOpenAlexId
            ORDER BY
                CASE WHEN NULLIF(LTRIM(RTRIM(m.WorkRawJson)), '') IS NULL THEN 1 ELSE 0 END,
                m.PilotOrder,
                m.FacultyId,
                m.FacultyIdentityCandidateWorkId DESC
        )
    FROM Missing m
)
SELECT
    PilotOrder,
    FacultyId,
    FacultyDisplayName,
    AuthorOpenAlexId,
    FacultyIdentityCandidateWorkId,
    WorkOpenAlexId,
    WorkDisplayName,
    WorkRawJson
INTO #SelectedPromotionRows
FROM Ranked
WHERE rn = 1;

DECLARE @MissingWorkCount INT =
(
    SELECT COUNT(*) FROM #SelectedPromotionRows
);

IF @MissingWorkCount <> @ExpectedMissingWorkCount
BEGIN
    DROP TABLE #SelectedPromotionRows;
    RAISERROR('Missing normalized work count changed after preflight.', 16, 1);
    RETURN;
END;

IF @MissingWorkCount > @MaxWorks
BEGIN
    DROP TABLE #SelectedPromotionRows;
    RAISERROR('Missing normalized work count exceeds MaxWorks.', 16, 1);
    RETURN;
END;

IF EXISTS
(
    SELECT 1
    FROM #SelectedPromotionRows
    WHERE NULLIF(LTRIM(RTRIM(WorkRawJson)), '') IS NULL
)
BEGIN
    DROP TABLE #SelectedPromotionRows;
    RAISERROR('One or more selected works has no WorkRawJson.', 16, 1);
    RETURN;
END;

SELECT
    PilotOrder,
    FacultyId,
    FacultyDisplayName,
    AuthorOpenAlexId,
    FacultyIdentityCandidateWorkId,
    WorkOpenAlexId,
    WorkDisplayName,
    WorkRawJson
FROM #SelectedPromotionRows
ORDER BY PilotOrder, FacultyId, WorkOpenAlexId;

DROP TABLE #SelectedPromotionRows;";

        command.Parameters.AddWithValue("@CohortName", cohortName);
        command.Parameters.AddWithValue("@ExpectedFacultyCount", expectedFacultyCount);
        command.Parameters.AddWithValue("@ExpectedMissingWorkCount", expectedMissingWorkCount);
        command.Parameters.AddWithValue("@MaxWorks", maxWorks);

        var rows = new List<AcceptedIdentityWorkPromotionSourceRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AcceptedIdentityWorkPromotionSourceRow
            {
                PilotOrder = reader.GetInt32(reader.GetOrdinal("PilotOrder")),
                FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
                FacultyDisplayName = reader.GetString(reader.GetOrdinal("FacultyDisplayName")),
                AuthorOpenAlexId = reader.GetString(reader.GetOrdinal("AuthorOpenAlexId")),
                FacultyIdentityCandidateWorkId = reader.GetInt64(reader.GetOrdinal("FacultyIdentityCandidateWorkId")),
                WorkOpenAlexId = reader.GetString(reader.GetOrdinal("WorkOpenAlexId")),
                WorkDisplayName = reader.IsDBNull(reader.GetOrdinal("WorkDisplayName"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("WorkDisplayName")),
                WorkRawJson = reader.GetString(reader.GetOrdinal("WorkRawJson"))
            });
        }

        return rows;
    }

    public async Task SaveItemAsync(
        long promotionRunId,
        AcceptedIdentityWorkPromotionSourceRow source,
        long workId,
        bool inserted,
        bool updated,
        int authorshipRows,
        int authorshipInstitutionRows,
        int matchedInstitutionRows,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcceptedIdentityWorkPromotionItem
(
    AcceptedIdentityWorkPromotionRunId,
    PilotOrder,
    FacultyId,
    FacultyDisplayName,
    AuthorOpenAlexId,
    SourceFacultyIdentityCandidateWorkId,
    WorkOpenAlexId,
    WorkDisplayName,
    WorkIdAfter,
    PromotionStatus,
    AuthorshipRows,
    AuthorshipInstitutionRows,
    MatchedInstitutionRows,
    CreatedAtUtc
)
VALUES
(
    @PromotionRunId,
    @PilotOrder,
    @FacultyId,
    @FacultyDisplayName,
    @AuthorOpenAlexId,
    @SourceFacultyIdentityCandidateWorkId,
    @WorkOpenAlexId,
    @WorkDisplayName,
    @WorkIdAfter,
    @PromotionStatus,
    @AuthorshipRows,
    @AuthorshipInstitutionRows,
    @MatchedInstitutionRows,
    SYSUTCDATETIME()
);";

        command.Parameters.AddWithValue("@PromotionRunId", promotionRunId);
        command.Parameters.AddWithValue("@PilotOrder", source.PilotOrder);
        command.Parameters.AddWithValue("@FacultyId", source.FacultyId);
        command.Parameters.AddWithValue("@FacultyDisplayName", source.FacultyDisplayName);
        command.Parameters.AddWithValue("@AuthorOpenAlexId", source.AuthorOpenAlexId);
        command.Parameters.AddWithValue("@SourceFacultyIdentityCandidateWorkId", source.FacultyIdentityCandidateWorkId);
        command.Parameters.AddWithValue("@WorkOpenAlexId", source.WorkOpenAlexId);
        command.Parameters.AddWithValue("@WorkDisplayName", (object?)source.WorkDisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue("@WorkIdAfter", workId);
        command.Parameters.AddWithValue("@PromotionStatus", inserted ? "Inserted" : updated ? "Updated" : "NoChange");
        command.Parameters.AddWithValue("@AuthorshipRows", authorshipRows);
        command.Parameters.AddWithValue("@AuthorshipInstitutionRows", authorshipInstitutionRows);
        command.Parameters.AddWithValue("@MatchedInstitutionRows", matchedInstitutionRows);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(
        long promotionRunId,
        AcceptedIdentityWorkPromotionResult result,
        AcceptedIdentityWorkPromotionSafetyCounts safetyAfter,
        string summary,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.AcceptedIdentityWorkPromotionRun
SET
    SelectedWorkCount = @SelectedWorkCount,
    InsertedWorkCount = @InsertedWorkCount,
    UpdatedWorkCount = @UpdatedWorkCount,
    AuthorshipRows = @AuthorshipRows,
    AuthorshipInstitutionRows = @AuthorshipInstitutionRows,
    MatchedInstitutionRows = @MatchedInstitutionRows,
    FailedItemCount = 0,
    WorkRowsAfter = @WorkRowsAfter,
    PublicationCandidateRowsAfter = @PublicationCandidateRowsAfter,
    PublicationDecisionRowsAfter = @PublicationDecisionRowsAfter,
    RunStatus = 'Completed',
    SummaryText = @SummaryText,
    ErrorText = NULL,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcceptedIdentityWorkPromotionRunId = @PromotionRunId;";

        command.Parameters.AddWithValue("@PromotionRunId", promotionRunId);
        command.Parameters.AddWithValue("@SelectedWorkCount", result.SelectedWorkCount);
        command.Parameters.AddWithValue("@InsertedWorkCount", result.InsertedWorkCount);
        command.Parameters.AddWithValue("@UpdatedWorkCount", result.UpdatedWorkCount);
        command.Parameters.AddWithValue("@AuthorshipRows", result.AuthorshipRows);
        command.Parameters.AddWithValue("@AuthorshipInstitutionRows", result.AuthorshipInstitutionRows);
        command.Parameters.AddWithValue("@MatchedInstitutionRows", result.MatchedInstitutionRows);
        command.Parameters.AddWithValue("@WorkRowsAfter", safetyAfter.WorkRows);
        command.Parameters.AddWithValue("@PublicationCandidateRowsAfter", safetyAfter.PublicationCandidateRows);
        command.Parameters.AddWithValue("@PublicationDecisionRowsAfter", safetyAfter.PublicationDecisionRows);
        command.Parameters.AddWithValue("@SummaryText", summary);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FailRunAsync(
        long promotionRunId,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.AcceptedIdentityWorkPromotionRun
SET
    FailedItemCount =
    (
        SELECT COUNT(*)
        FROM openalex.AcceptedIdentityWorkPromotionItem
        WHERE AcceptedIdentityWorkPromotionRunId = @PromotionRunId
          AND PromotionStatus = 'Failed'
    ),
    RunStatus = 'Failed',
    ErrorText = @ErrorText,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AcceptedIdentityWorkPromotionRunId = @PromotionRunId;";

        command.Parameters.AddWithValue("@PromotionRunId", promotionRunId);
        command.Parameters.AddWithValue("@ErrorText", exception.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveFailedItemAsync(
        long promotionRunId,
        AcceptedIdentityWorkPromotionSourceRow source,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.AcceptedIdentityWorkPromotionItem
(
    AcceptedIdentityWorkPromotionRunId,
    PilotOrder,
    FacultyId,
    FacultyDisplayName,
    AuthorOpenAlexId,
    SourceFacultyIdentityCandidateWorkId,
    WorkOpenAlexId,
    WorkDisplayName,
    PromotionStatus,
    ErrorText,
    CreatedAtUtc
)
VALUES
(
    @PromotionRunId,
    @PilotOrder,
    @FacultyId,
    @FacultyDisplayName,
    @AuthorOpenAlexId,
    @SourceFacultyIdentityCandidateWorkId,
    @WorkOpenAlexId,
    @WorkDisplayName,
    'Failed',
    @ErrorText,
    SYSUTCDATETIME()
);";

        command.Parameters.AddWithValue("@PromotionRunId", promotionRunId);
        command.Parameters.AddWithValue("@PilotOrder", source.PilotOrder);
        command.Parameters.AddWithValue("@FacultyId", source.FacultyId);
        command.Parameters.AddWithValue("@FacultyDisplayName", source.FacultyDisplayName);
        command.Parameters.AddWithValue("@AuthorOpenAlexId", source.AuthorOpenAlexId);
        command.Parameters.AddWithValue("@SourceFacultyIdentityCandidateWorkId", source.FacultyIdentityCandidateWorkId);
        command.Parameters.AddWithValue("@WorkOpenAlexId", source.WorkOpenAlexId);
        command.Parameters.AddWithValue("@WorkDisplayName", (object?)source.WorkDisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue("@ErrorText", exception.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureAuditTablesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT CASE
    WHEN OBJECT_ID(N'openalex.AcceptedIdentityWorkPromotionRun', N'U') IS NOT NULL
     AND OBJECT_ID(N'openalex.AcceptedIdentityWorkPromotionItem', N'U') IS NOT NULL
    THEN 1 ELSE 0 END;";

        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new InvalidOperationException(
                "PATCH017B10A promotion audit tables do not exist. Run sql/400_V3E_PATCH017B10A_AcceptedIdentityWorkPromotionExecution_SQL2016.sql first.");
        }
    }
}

public sealed class AcceptedIdentityWorkPromotionSourceRow
{
    public int PilotOrder { get; set; }
    public long FacultyId { get; set; }
    public string FacultyDisplayName { get; set; } = string.Empty;
    public string AuthorOpenAlexId { get; set; } = string.Empty;
    public long FacultyIdentityCandidateWorkId { get; set; }
    public string WorkOpenAlexId { get; set; } = string.Empty;
    public string? WorkDisplayName { get; set; }
    public string WorkRawJson { get; set; } = string.Empty;
}

public sealed class AcceptedIdentityWorkPromotionSafetyCounts
{
    public int WorkRows { get; set; }
    public int PublicationCandidateRows { get; set; }
    public int PublicationDecisionRows { get; set; }
}

public sealed class AcceptedIdentityWorkPromotionResult
{
    public int SelectedWorkCount { get; set; }
    public int InsertedWorkCount { get; set; }
    public int UpdatedWorkCount { get; set; }
    public int AuthorshipRows { get; set; }
    public int AuthorshipInstitutionRows { get; set; }
    public int MatchedInstitutionRows { get; set; }
}
