using System.Data;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class FacultyAuthorIdentityResolutionPreviewRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyAuthorIdentityResolutionPreviewRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<FacultyAuthorIdentityResolutionSafetyCounts> GetSafetyCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    IdentityCandidateRows = CASE WHEN OBJECT_ID(N'openalex.FacultyIdentityCandidate', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyIdentityCandidate) END,
    IdentityDecisionRows = CASE WHEN OBJECT_ID(N'openalex.FacultyIdentityCandidateDecision', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyIdentityCandidateDecision) END,
    AuthorDecisionRows = CASE WHEN OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM openalex.FacultyAuthorMatchDecision) END;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new FacultyAuthorIdentityResolutionSafetyCounts
        {
            IdentityCandidateRows = reader.GetInt32(0),
            IdentityDecisionRows = reader.GetInt32(1),
            AuthorDecisionRows = reader.GetInt32(2)
        };
    }

    public async Task<IReadOnlyList<FacultyAuthorIdentityResolutionPreviewSourceRow>> LoadSourceRowsAsync(
        long sourceWorkBackfillRunId,
        long facultyId,
        int maxWorks,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<FacultyAuthorIdentityResolutionPreviewSourceRow>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.AcademicOnlineWorkBackfillRun", "U", cancellationToken) ||
            !await ObjectExistsAsync(connection, "openalex.AcademicOnlineWorkBackfillItem", "U", cancellationToken))
        {
            throw new InvalidOperationException("PATCH017B1 backfill tables do not exist. Run sql/372_V3E_PATCH017B1_ControlledAcademicOnlineGuidedWorkBackfill_SQL2016.sql first.");
        }

        await EnsureTablesAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
;WITH RankedWorks AS
(
    SELECT
        bi.WorkIdAfter AS WorkId,
        ROW_NUMBER() OVER (ORDER BY bi.AcademicOnlineWorkBackfillItemId) AS WorkRank
    FROM openalex.AcademicOnlineWorkBackfillItem bi
    JOIN openalex.AcademicOnlineWorkBackfillRun br
        ON br.AcademicOnlineWorkBackfillRunId = bi.AcademicOnlineWorkBackfillRunId
    WHERE bi.AcademicOnlineWorkBackfillRunId = @SourceWorkBackfillRunId
      AND bi.FacultyId = @FacultyId
      AND br.RunStatus = 'Completed'
      AND bi.WorkIdAfter IS NOT NULL
      AND bi.ExecutionStatus IN ('Inserted', 'ConcurrentExistingWorkUpdated', 'SkippedAlreadyExists')
),
SelectedWorks AS
(
    SELECT WorkId
    FROM RankedWorks
    WHERE WorkRank <= @MaxWorks
),
LatestAcademicOnline AS
(
    SELECT TOP (1)
        s.AcademicOnlineName,
        s.AcademicOnlineEmail,
        s.AcademicOnlineOrcidUrl
    FROM openalex.AcademicOnlineScholarSnapshot s
    WHERE s.FacultyId = @FacultyId
    ORDER BY s.AcademicOnlineComparisonRunId DESC, s.AcademicOnlineScholarSnapshotId DESC
),
CandidateAuthors AS
(
    SELECT
        wa.AuthorOpenAlexId,
        CandidateAuthorDisplayName = MAX(wa.AuthorDisplayName),
        CandidateAuthorOrcid = MAX(wa.AuthorOrcid),
        WorkAppearanceCount = COUNT(DISTINCT wa.WorkId),
        CorrespondingAuthorWorkCount = SUM(CASE WHEN wa.IsCorresponding = 1 THEN 1 ELSE 0 END),
        AuthorshipInstitutionCount = SUM
        (
            (SELECT COUNT(*)
             FROM openalex.WorkAuthorshipInstitution wai
             WHERE wai.WorkAuthorshipId = wa.WorkAuthorshipId)
        ),
        UvaAffiliationWorkCount = SUM
        (
            CASE WHEN EXISTS
            (
                SELECT 1
                FROM openalex.WorkAuthorshipInstitution wai
                WHERE wai.WorkAuthorshipId = wa.WorkAuthorshipId
                  AND
                  (
                      wai.InstitutionDisplayName LIKE '%University of Virginia%'
                      OR wai.InstitutionOpenAlexId IN
                      (
                          'https://openalex.org/I4210107925',
                          'https://openalex.org/I4210119250',
                          'https://openalex.org/I4210134211'
                      )
                  )
            ) THEN 1 ELSE 0 END
        )
    FROM openalex.WorkAuthorship wa
    JOIN SelectedWorks sw ON sw.WorkId = wa.WorkId
    WHERE NULLIF(LTRIM(RTRIM(wa.AuthorOpenAlexId)), '') IS NOT NULL
    GROUP BY wa.AuthorOpenAlexId
),
SourceWorkCount AS
(
    SELECT COUNT(*) AS WorkCount FROM SelectedWorks
)
SELECT
    @SourceWorkBackfillRunId AS SourceWorkBackfillRunId,
    f.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    f.FirstName AS FacultyFirstName,
    f.LastName AS FacultyLastName,
    f.Orcid AS FacultyOrcid,
    f.ClaimedOpenAlexAuthorId AS FacultyClaimedOpenAlexAuthorId,
    f.DepartmentName,
    ao.AcademicOnlineName,
    ao.AcademicOnlineEmail,
    ao.AcademicOnlineOrcidUrl,
    swc.WorkCount AS SourceWorkCount,
    ca.AuthorOpenAlexId AS CandidateAuthorOpenAlexId,
    ca.CandidateAuthorDisplayName,
    ca.CandidateAuthorOrcid,
    ca.WorkAppearanceCount,
    ca.CorrespondingAuthorWorkCount,
    ca.AuthorshipInstitutionCount,
    ca.UvaAffiliationWorkCount,
    AppearingWorkOpenAlexIds = STUFF
    (
        (
            SELECT ',' + w2.OpenAlexId
            FROM openalex.WorkAuthorship wa2
            JOIN SelectedWorks sw2 ON sw2.WorkId = wa2.WorkId
            JOIN openalex.Work w2 ON w2.WorkId = wa2.WorkId
            WHERE wa2.AuthorOpenAlexId = ca.AuthorOpenAlexId
            ORDER BY w2.WorkId
            FOR XML PATH(''), TYPE
        ).value('.', 'nvarchar(max)'),
        1,
        1,
        ''
    ),
    s.FacultyIdentityCandidateSummaryId AS ExistingIdentitySummaryId,
    s.IdentityReviewTriageStatus AS ExistingIdentityTriageStatus,
    s.BestIdentityEvidenceScore AS ExistingIdentityEvidenceScore,
    idd.DecisionStatus AS CurrentIdentityDecisionStatus,
    ExistingIdentityCandidateDiscoveryCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyIdentityCandidate fic
        WHERE fic.FacultyId = f.FacultyId
          AND fic.CandidateAuthorOpenAlexId = ca.AuthorOpenAlexId
    ),
    CurrentAcceptedAuthorDecisionCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyAuthorMatchDecision fad
        WHERE fad.FacultyId = f.FacultyId
          AND fad.AuthorOpenAlexId = ca.AuthorOpenAlexId
          AND fad.DecisionStatus = 'Accepted'
          AND fad.IsCurrent = 1
    )
FROM CandidateAuthors ca
CROSS JOIN SourceWorkCount swc
JOIN openalex.Faculty f ON f.FacultyId = @FacultyId
OUTER APPLY (SELECT * FROM LatestAcademicOnline) ao
OUTER APPLY
(
    SELECT TOP (1)
        s2.FacultyIdentityCandidateSummaryId,
        s2.IdentityReviewTriageStatus,
        s2.BestIdentityEvidenceScore
    FROM openalex.FacultyIdentityCandidateSummary s2
    WHERE s2.FacultyId = f.FacultyId
      AND s2.CandidateAuthorOpenAlexId = ca.AuthorOpenAlexId
    ORDER BY s2.BestIdentityEvidenceScore DESC, s2.FacultyIdentityCandidateSummaryId DESC
) s
OUTER APPLY
(
    SELECT TOP (1) d.DecisionStatus
    FROM openalex.FacultyIdentityCandidateDecision d
    WHERE d.FacultyId = f.FacultyId
      AND d.CandidateAuthorOpenAlexId = ca.AuthorOpenAlexId
      AND d.IsCurrent = 1
    ORDER BY d.FacultyIdentityCandidateDecisionId DESC
) idd
ORDER BY ca.WorkAppearanceCount DESC, ca.UvaAffiliationWorkCount DESC, ca.CandidateAuthorDisplayName;";

        command.Parameters.AddWithValue("@SourceWorkBackfillRunId", sourceWorkBackfillRunId);
        command.Parameters.AddWithValue("@FacultyId", facultyId);
        command.Parameters.AddWithValue("@MaxWorks", Math.Clamp(maxWorks, 1, 5));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FacultyAuthorIdentityResolutionPreviewSourceRow
            {
                SourceWorkBackfillRunId = reader.GetInt64(reader.GetOrdinal("SourceWorkBackfillRunId")),
                FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
                FacultyDisplayName = reader.GetString(reader.GetOrdinal("FacultyDisplayName")),
                FacultyFirstName = GetNullableString(reader, "FacultyFirstName"),
                FacultyLastName = GetNullableString(reader, "FacultyLastName"),
                FacultyOrcid = GetNullableString(reader, "FacultyOrcid"),
                FacultyClaimedOpenAlexAuthorId = GetNullableString(reader, "FacultyClaimedOpenAlexAuthorId"),
                DepartmentName = GetNullableString(reader, "DepartmentName"),
                AcademicOnlineName = GetNullableString(reader, "AcademicOnlineName"),
                AcademicOnlineEmail = GetNullableString(reader, "AcademicOnlineEmail"),
                AcademicOnlineOrcidUrl = GetNullableString(reader, "AcademicOnlineOrcidUrl"),
                SourceWorkCount = reader.GetInt32(reader.GetOrdinal("SourceWorkCount")),
                CandidateAuthorOpenAlexId = reader.GetString(reader.GetOrdinal("CandidateAuthorOpenAlexId")),
                CandidateAuthorDisplayName = GetNullableString(reader, "CandidateAuthorDisplayName"),
                CandidateAuthorOrcid = GetNullableString(reader, "CandidateAuthorOrcid"),
                WorkAppearanceCount = reader.GetInt32(reader.GetOrdinal("WorkAppearanceCount")),
                CorrespondingAuthorWorkCount = reader.GetInt32(reader.GetOrdinal("CorrespondingAuthorWorkCount")),
                AuthorshipInstitutionCount = reader.GetInt32(reader.GetOrdinal("AuthorshipInstitutionCount")),
                UvaAffiliationWorkCount = reader.GetInt32(reader.GetOrdinal("UvaAffiliationWorkCount")),
                AppearingWorkOpenAlexIds = GetNullableString(reader, "AppearingWorkOpenAlexIds"),
                ExistingIdentitySummaryId = GetNullableInt64(reader, "ExistingIdentitySummaryId"),
                ExistingIdentityTriageStatus = GetNullableString(reader, "ExistingIdentityTriageStatus"),
                ExistingIdentityEvidenceScore = GetNullableDecimal(reader, "ExistingIdentityEvidenceScore"),
                CurrentIdentityDecisionStatus = GetNullableString(reader, "CurrentIdentityDecisionStatus"),
                ExistingIdentityCandidateDiscoveryCount = reader.GetInt32(reader.GetOrdinal("ExistingIdentityCandidateDiscoveryCount")),
                CurrentAcceptedAuthorDecisionCount = reader.GetInt32(reader.GetOrdinal("CurrentAcceptedAuthorDecisionCount"))
            });
        }

        return rows;
    }

    public async Task<long> StartRunAsync(
        long sourceWorkBackfillRunId,
        long facultyId,
        int maxWorks,
        FacultyAuthorIdentityResolutionSafetyCounts safetyBefore,
        string startedBy,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureTablesAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.FacultyAuthorIdentityResolutionPreviewRun
(
    SourceWorkBackfillRunId,
    FacultyId,
    MaxWorks,
    IdentityCandidateRowsBefore,
    IdentityDecisionRowsBefore,
    AuthorDecisionRowsBefore,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    StartedAtUtc,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.FacultyAuthorIdentityResolutionPreviewRunId
VALUES
(
    @SourceWorkBackfillRunId,
    @FacultyId,
    @MaxWorks,
    @IdentityCandidateRowsBefore,
    @IdentityDecisionRowsBefore,
    @AuthorDecisionRowsBefore,
    'Running',
    @StartedBy,
    @ApplicationVersion,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@SourceWorkBackfillRunId", sourceWorkBackfillRunId);
        command.Parameters.AddWithValue("@FacultyId", facultyId);
        command.Parameters.AddWithValue("@MaxWorks", maxWorks);
        command.Parameters.AddWithValue("@IdentityCandidateRowsBefore", safetyBefore.IdentityCandidateRows);
        command.Parameters.AddWithValue("@IdentityDecisionRowsBefore", safetyBefore.IdentityDecisionRows);
        command.Parameters.AddWithValue("@AuthorDecisionRowsBefore", safetyBefore.AuthorDecisionRows);
        command.Parameters.AddWithValue("@StartedBy", startedBy);
        command.Parameters.AddWithValue("@ApplicationVersion", applicationVersion);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task SaveCandidateAsync(
        long previewRunId,
        FacultyAuthorIdentityResolutionPreviewSourceRow row,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.FacultyAuthorIdentityResolutionPreviewCandidate
(
    FacultyAuthorIdentityResolutionPreviewRunId,
    SourceWorkBackfillRunId,
    FacultyId,
    FacultyDisplayName,
    FacultyOrcid,
    FacultyClaimedOpenAlexAuthorId,
    AcademicOnlineName,
    AcademicOnlineEmail,
    AcademicOnlineOrcidUrl,
    SourceWorkCount,
    CandidateAuthorOpenAlexId,
    CandidateAuthorDisplayName,
    CandidateAuthorOrcid,
    WorkAppearanceCount,
    CorrespondingAuthorWorkCount,
    AuthorshipInstitutionCount,
    UvaAffiliationWorkCount,
    AppearingWorkOpenAlexIds,
    ExistingIdentitySummaryId,
    ExistingIdentityTriageStatus,
    ExistingIdentityEvidenceScore,
    CurrentIdentityDecisionStatus,
    ExistingIdentityCandidateDiscoveryCount,
    CurrentAcceptedAuthorDecisionCount,
    ExactFacultyOrcidMatch,
    ExactAcademicOnlineOrcidMatch,
    ExactClaimedOpenAlexIdMatch,
    NameEvidenceScore,
    AppearsOnAllSelectedWorks,
    ProposedIdentityScore,
    CandidateRank,
    PreviewStatus,
    ProposedNextAction,
    EvidenceSummary,
    EvidenceJson,
    CreatedAtUtc
)
VALUES
(
    @PreviewRunId,
    @SourceWorkBackfillRunId,
    @FacultyId,
    @FacultyDisplayName,
    @FacultyOrcid,
    @FacultyClaimedOpenAlexAuthorId,
    @AcademicOnlineName,
    @AcademicOnlineEmail,
    @AcademicOnlineOrcidUrl,
    @SourceWorkCount,
    @CandidateAuthorOpenAlexId,
    @CandidateAuthorDisplayName,
    @CandidateAuthorOrcid,
    @WorkAppearanceCount,
    @CorrespondingAuthorWorkCount,
    @AuthorshipInstitutionCount,
    @UvaAffiliationWorkCount,
    @AppearingWorkOpenAlexIds,
    @ExistingIdentitySummaryId,
    @ExistingIdentityTriageStatus,
    @ExistingIdentityEvidenceScore,
    @CurrentIdentityDecisionStatus,
    @ExistingIdentityCandidateDiscoveryCount,
    @CurrentAcceptedAuthorDecisionCount,
    @ExactFacultyOrcidMatch,
    @ExactAcademicOnlineOrcidMatch,
    @ExactClaimedOpenAlexIdMatch,
    @NameEvidenceScore,
    @AppearsOnAllSelectedWorks,
    @ProposedIdentityScore,
    @CandidateRank,
    @PreviewStatus,
    @ProposedNextAction,
    @EvidenceSummary,
    @EvidenceJson,
    SYSUTCDATETIME()
);";

        command.Parameters.AddWithValue("@PreviewRunId", previewRunId);
        command.Parameters.AddWithValue("@SourceWorkBackfillRunId", row.SourceWorkBackfillRunId);
        command.Parameters.AddWithValue("@FacultyId", row.FacultyId);
        command.Parameters.AddWithValue("@FacultyDisplayName", row.FacultyDisplayName);
        AddNullable(command, "@FacultyOrcid", SqlDbType.NVarChar, 256, row.FacultyOrcid);
        AddNullable(command, "@FacultyClaimedOpenAlexAuthorId", SqlDbType.NVarChar, 128, row.FacultyClaimedOpenAlexAuthorId);
        AddNullable(command, "@AcademicOnlineName", SqlDbType.NVarChar, 512, row.AcademicOnlineName);
        AddNullable(command, "@AcademicOnlineEmail", SqlDbType.NVarChar, 512, row.AcademicOnlineEmail);
        AddNullable(command, "@AcademicOnlineOrcidUrl", SqlDbType.NVarChar, 512, row.AcademicOnlineOrcidUrl);
        command.Parameters.AddWithValue("@SourceWorkCount", row.SourceWorkCount);
        command.Parameters.AddWithValue("@CandidateAuthorOpenAlexId", row.CandidateAuthorOpenAlexId);
        AddNullable(command, "@CandidateAuthorDisplayName", SqlDbType.NVarChar, 512, row.CandidateAuthorDisplayName);
        AddNullable(command, "@CandidateAuthorOrcid", SqlDbType.NVarChar, 256, row.CandidateAuthorOrcid);
        command.Parameters.AddWithValue("@WorkAppearanceCount", row.WorkAppearanceCount);
        command.Parameters.AddWithValue("@CorrespondingAuthorWorkCount", row.CorrespondingAuthorWorkCount);
        command.Parameters.AddWithValue("@AuthorshipInstitutionCount", row.AuthorshipInstitutionCount);
        command.Parameters.AddWithValue("@UvaAffiliationWorkCount", row.UvaAffiliationWorkCount);
        AddNullable(command, "@AppearingWorkOpenAlexIds", SqlDbType.NVarChar, -1, row.AppearingWorkOpenAlexIds);
        AddNullable(command, "@ExistingIdentitySummaryId", SqlDbType.BigInt, row.ExistingIdentitySummaryId);
        AddNullable(command, "@ExistingIdentityTriageStatus", SqlDbType.NVarChar, 100, row.ExistingIdentityTriageStatus);
        AddNullable(command, "@ExistingIdentityEvidenceScore", SqlDbType.Decimal, row.ExistingIdentityEvidenceScore);
        AddNullable(command, "@CurrentIdentityDecisionStatus", SqlDbType.NVarChar, 100, row.CurrentIdentityDecisionStatus);
        command.Parameters.AddWithValue("@ExistingIdentityCandidateDiscoveryCount", row.ExistingIdentityCandidateDiscoveryCount);
        command.Parameters.AddWithValue("@CurrentAcceptedAuthorDecisionCount", row.CurrentAcceptedAuthorDecisionCount);
        command.Parameters.AddWithValue("@ExactFacultyOrcidMatch", row.ExactFacultyOrcidMatch);
        command.Parameters.AddWithValue("@ExactAcademicOnlineOrcidMatch", row.ExactAcademicOnlineOrcidMatch);
        command.Parameters.AddWithValue("@ExactClaimedOpenAlexIdMatch", row.ExactClaimedOpenAlexIdMatch);
        command.Parameters.AddWithValue("@NameEvidenceScore", row.NameEvidenceScore);
        command.Parameters.AddWithValue("@AppearsOnAllSelectedWorks", row.AppearsOnAllSelectedWorks);
        command.Parameters.AddWithValue("@ProposedIdentityScore", row.ProposedIdentityScore);
        command.Parameters.AddWithValue("@CandidateRank", row.CandidateRank);
        command.Parameters.AddWithValue("@PreviewStatus", row.PreviewStatus);
        command.Parameters.AddWithValue("@ProposedNextAction", row.ProposedNextAction);
        command.Parameters.AddWithValue("@EvidenceSummary", row.EvidenceSummary);
        command.Parameters.AddWithValue("@EvidenceJson", row.EvidenceJson);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(
        long previewRunId,
        int selectedWorks,
        int evaluatedCandidates,
        int strongCandidates,
        int exactOrcidCandidates,
        int ambiguousCandidates,
        FacultyAuthorIdentityResolutionSafetyCounts safetyAfter,
        string summary,
        CancellationToken cancellationToken = default)
    {
        await UpdateRunAsync(previewRunId, "Completed", selectedWorks, evaluatedCandidates, strongCandidates, exactOrcidCandidates, ambiguousCandidates, safetyAfter, summary, null, cancellationToken);
    }

    public async Task FailRunAsync(long previewRunId, Exception exception, CancellationToken cancellationToken = default)
    {
        await UpdateRunAsync(previewRunId, "Failed", 0, 0, 0, 0, 0, null, null, exception.ToString(), cancellationToken);
    }

    private async Task UpdateRunAsync(
        long previewRunId,
        string status,
        int selectedWorks,
        int evaluatedCandidates,
        int strongCandidates,
        int exactOrcidCandidates,
        int ambiguousCandidates,
        FacultyAuthorIdentityResolutionSafetyCounts? safetyAfter,
        string? summary,
        string? error,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.FacultyAuthorIdentityResolutionPreviewRun
SET
    SelectedWorkCount = @SelectedWorkCount,
    EvaluatedCandidateCount = @EvaluatedCandidateCount,
    StrongCandidateCount = @StrongCandidateCount,
    ExactOrcidCandidateCount = @ExactOrcidCandidateCount,
    AmbiguousCandidateCount = @AmbiguousCandidateCount,
    IdentityCandidateRowsAfter = @IdentityCandidateRowsAfter,
    IdentityDecisionRowsAfter = @IdentityDecisionRowsAfter,
    AuthorDecisionRowsAfter = @AuthorDecisionRowsAfter,
    RunStatus = @Status,
    SummaryText = @Summary,
    ErrorText = @Error,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyAuthorIdentityResolutionPreviewRunId = @PreviewRunId;";
        command.Parameters.AddWithValue("@PreviewRunId", previewRunId);
        command.Parameters.AddWithValue("@SelectedWorkCount", selectedWorks);
        command.Parameters.AddWithValue("@EvaluatedCandidateCount", evaluatedCandidates);
        command.Parameters.AddWithValue("@StrongCandidateCount", strongCandidates);
        command.Parameters.AddWithValue("@ExactOrcidCandidateCount", exactOrcidCandidates);
        command.Parameters.AddWithValue("@AmbiguousCandidateCount", ambiguousCandidates);
        command.Parameters.AddWithValue("@IdentityCandidateRowsAfter", safetyAfter is null ? DBNull.Value : safetyAfter.IdentityCandidateRows);
        command.Parameters.AddWithValue("@IdentityDecisionRowsAfter", safetyAfter is null ? DBNull.Value : safetyAfter.IdentityDecisionRows);
        command.Parameters.AddWithValue("@AuthorDecisionRowsAfter", safetyAfter is null ? DBNull.Value : safetyAfter.AuthorDecisionRows);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@Summary", (object?)summary ?? DBNull.Value);
        command.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT CASE
    WHEN OBJECT_ID(N'openalex.FacultyAuthorIdentityResolutionPreviewRun', N'U') IS NOT NULL
     AND OBJECT_ID(N'openalex.FacultyAuthorIdentityResolutionPreviewCandidate', N'U') IS NOT NULL
    THEN 1 ELSE 0 END;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new InvalidOperationException("PATCH017B3 preview tables do not exist. Run sql/376_V3E_PATCH017B3_FacultyAuthorIdentityResolutionPreview_SQL2016.sql first.");
        }
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static long? GetNullableInt64(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static decimal? GetNullableDecimal(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, int size, string? value)
    {
        var parameter = command.Parameters.Add(name, type, size);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, long? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, decimal? value)
    {
        var parameter = command.Parameters.Add(name, type);
        if (type == SqlDbType.Decimal)
        {
            parameter.Precision = 18;
            parameter.Scale = 4;
        }
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }
}
