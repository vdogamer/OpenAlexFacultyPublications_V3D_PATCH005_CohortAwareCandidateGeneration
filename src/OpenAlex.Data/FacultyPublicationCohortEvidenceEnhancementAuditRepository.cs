using Microsoft.Data.SqlClient;

namespace OpenAlex.Data;

public sealed class FacultyPublicationCohortEvidenceEnhancementAuditRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyPublicationCohortEvidenceEnhancementAuditRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<FacultyPublicationCohortEvidenceSafetyCounts> GetSafetyCountsAsync(
        long sourceCandidateIngestionRunId,
        string cohortName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    CandidateRowsTotal = (SELECT COUNT(*) FROM openalex.FacultyPublicationCandidate),
    PublicationDecisionRows = (SELECT COUNT(*) FROM openalex.FacultyPublicationDecision),
    SourceCandidateRows =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyPublicationCandidate pc
        WHERE pc.IngestionRunId = @SourceCandidateIngestionRunId
          AND EXISTS
          (
              SELECT 1
              FROM openalex.PilotFacultyCohort cohort
              WHERE cohort.FacultyId = pc.FacultyId
                AND cohort.CohortName = @CohortName
                AND cohort.IsEnabled = 1
          )
    ),
    SourceCandidatesWithPatch007Evidence =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyPublicationCandidate pc
        WHERE pc.IngestionRunId = @SourceCandidateIngestionRunId
          AND pc.EvidenceSummary LIKE '%PATCH007Evidence:%'
          AND EXISTS
          (
              SELECT 1
              FROM openalex.PilotFacultyCohort cohort
              WHERE cohort.FacultyId = pc.FacultyId
                AND cohort.CohortName = @CohortName
                AND cohort.IsEnabled = 1
          )
    );";
        command.Parameters.AddWithValue("@SourceCandidateIngestionRunId", sourceCandidateIngestionRunId);
        command.Parameters.AddWithValue("@CohortName", cohortName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Could not read PATCH017B11A safety counts.");
        }

        return new FacultyPublicationCohortEvidenceSafetyCounts
        {
            CandidateRowsTotal = reader.GetInt32(0),
            PublicationDecisionRows = reader.GetInt32(1),
            SourceCandidateRows = reader.GetInt32(2),
            SourceCandidatesWithPatch007Evidence = reader.GetInt32(3)
        };
    }

    public async Task<long> StartRunAsync(
        string cohortName,
        long sourceCandidateIngestionRunId,
        int expectedCandidateCount,
        FacultyPublicationCohortEvidenceSafetyCounts before,
        string startedBy,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureAuditTableAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.FacultyPublicationCohortEvidenceEnhancementRun
(
    CohortName,
    SourceCandidateIngestionRunId,
    ExpectedCandidateCount,
    ConfirmedEnhancement,
    CandidateRowsTotalBefore,
    PublicationDecisionRowsBefore,
    SourceCandidateRowsBefore,
    SourceCandidatesWithEvidenceBefore,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    StartedAtUtc,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.FacultyPublicationCohortEvidenceEnhancementRunId
VALUES
(
    @CohortName,
    @SourceCandidateIngestionRunId,
    @ExpectedCandidateCount,
    1,
    @CandidateRowsTotalBefore,
    @PublicationDecisionRowsBefore,
    @SourceCandidateRowsBefore,
    @SourceCandidatesWithEvidenceBefore,
    'Running',
    @StartedBy,
    @ApplicationVersion,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@CohortName", cohortName);
        command.Parameters.AddWithValue("@SourceCandidateIngestionRunId", sourceCandidateIngestionRunId);
        command.Parameters.AddWithValue("@ExpectedCandidateCount", expectedCandidateCount);
        command.Parameters.AddWithValue("@CandidateRowsTotalBefore", before.CandidateRowsTotal);
        command.Parameters.AddWithValue("@PublicationDecisionRowsBefore", before.PublicationDecisionRows);
        command.Parameters.AddWithValue("@SourceCandidateRowsBefore", before.SourceCandidateRows);
        command.Parameters.AddWithValue("@SourceCandidatesWithEvidenceBefore", before.SourceCandidatesWithPatch007Evidence);
        command.Parameters.AddWithValue("@StartedBy", startedBy);
        command.Parameters.AddWithValue("@ApplicationVersion", applicationVersion);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task CompleteRunAsync(
        long runId,
        FacultyPublicationCohortEvidenceSafetyCounts after,
        int evaluated,
        int enhanced,
        int institutionEvidence,
        int coauthorEvidence,
        string summary,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.FacultyPublicationCohortEvidenceEnhancementRun
SET
    CandidateRowsEvaluated = @CandidateRowsEvaluated,
    CandidateRowsEnhanced = @CandidateRowsEnhanced,
    RowsWithAffiliatedInstitutionEvidence = @RowsWithAffiliatedInstitutionEvidence,
    RowsWithKnownCoauthorEvidence = @RowsWithKnownCoauthorEvidence,
    CandidateRowsTotalAfter = @CandidateRowsTotalAfter,
    PublicationDecisionRowsAfter = @PublicationDecisionRowsAfter,
    SourceCandidateRowsAfter = @SourceCandidateRowsAfter,
    SourceCandidatesWithEvidenceAfter = @SourceCandidatesWithEvidenceAfter,
    RunStatus = 'Completed',
    SummaryText = @SummaryText,
    ErrorText = NULL,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyPublicationCohortEvidenceEnhancementRunId = @RunId;";
        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@CandidateRowsEvaluated", evaluated);
        command.Parameters.AddWithValue("@CandidateRowsEnhanced", enhanced);
        command.Parameters.AddWithValue("@RowsWithAffiliatedInstitutionEvidence", institutionEvidence);
        command.Parameters.AddWithValue("@RowsWithKnownCoauthorEvidence", coauthorEvidence);
        command.Parameters.AddWithValue("@CandidateRowsTotalAfter", after.CandidateRowsTotal);
        command.Parameters.AddWithValue("@PublicationDecisionRowsAfter", after.PublicationDecisionRows);
        command.Parameters.AddWithValue("@SourceCandidateRowsAfter", after.SourceCandidateRows);
        command.Parameters.AddWithValue("@SourceCandidatesWithEvidenceAfter", after.SourceCandidatesWithPatch007Evidence);
        command.Parameters.AddWithValue("@SummaryText", summary);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FailRunAsync(long runId, Exception exception, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.FacultyPublicationCohortEvidenceEnhancementRun
SET
    RunStatus = 'Failed',
    ErrorText = @ErrorText,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyPublicationCohortEvidenceEnhancementRunId = @RunId;";
        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@ErrorText", exception.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureAuditTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT CASE WHEN OBJECT_ID(
    N'openalex.FacultyPublicationCohortEvidenceEnhancementRun', N'U') IS NULL
    THEN 0 ELSE 1 END;";

        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new InvalidOperationException(
                "PATCH017B11A audit table does not exist. Run sql/404_V3E_PATCH017B11A_FiveFacultyPublicationEvidenceEnhancement_SQL2016.sql first.");
        }
    }
}

public sealed class FacultyPublicationCohortEvidenceSafetyCounts
{
    public int CandidateRowsTotal { get; set; }
    public int PublicationDecisionRows { get; set; }
    public int SourceCandidateRows { get; set; }
    public int SourceCandidatesWithPatch007Evidence { get; set; }
}
