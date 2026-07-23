using System.Data;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class FacultyPublicationCandidatePreviewRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyPublicationCandidatePreviewRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<BackfillSafetyCounts> GetSafetyCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    CandidateRows = (SELECT COUNT(*) FROM openalex.FacultyPublicationCandidate),
    DecisionRows = (SELECT COUNT(*) FROM openalex.FacultyPublicationDecision);";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Unable to read faculty-publication safety counts.");
        }

        return new BackfillSafetyCounts
        {
            CandidateRows = reader.GetInt32(0),
            DecisionRows = reader.GetInt32(1)
        };
    }

    public async Task<long> StartRunAsync(
        long sourceWorkBackfillRunId,
        long facultyId,
        int maxWorks,
        BackfillSafetyCounts safetyBefore,
        string startedBy,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureTablesAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
IF NOT EXISTS
(
    SELECT 1
    FROM openalex.AcademicOnlineWorkBackfillRun
    WHERE AcademicOnlineWorkBackfillRunId = @SourceRunId
      AND RunStatus = 'Completed'
)
BEGIN
    RAISERROR('The selected PATCH017B1 work backfill run does not exist or is not completed.', 16, 1);
    RETURN;
END;

INSERT INTO openalex.FacultyPublicationCandidatePreviewRun
(
    SourceWorkBackfillRunId,
    FacultyId,
    MaxWorks,
    CandidateRowsBefore,
    DecisionRowsBefore,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    StartedAtUtc,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.FacultyPublicationCandidatePreviewRunId
VALUES
(
    @SourceRunId,
    @FacultyId,
    @MaxWorks,
    @CandidateRowsBefore,
    @DecisionRowsBefore,
    'Running',
    @StartedBy,
    @ApplicationVersion,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@SourceRunId", sourceWorkBackfillRunId);
        command.Parameters.AddWithValue("@FacultyId", facultyId);
        command.Parameters.AddWithValue("@MaxWorks", maxWorks);
        command.Parameters.AddWithValue("@CandidateRowsBefore", safetyBefore.CandidateRows);
        command.Parameters.AddWithValue("@DecisionRowsBefore", safetyBefore.DecisionRows);
        command.Parameters.AddWithValue("@StartedBy", startedBy);
        command.Parameters.AddWithValue("@ApplicationVersion", applicationVersion);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value);
    }

    public async Task<IReadOnlyList<FacultyPublicationCandidatePreviewSourceRow>> LoadSourceRowsAsync(
        long sourceWorkBackfillRunId,
        long facultyId,
        int maxWorks,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<FacultyPublicationCandidatePreviewSourceRow>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP (@MaxWorks)
    SourceBackfillItemId = i.AcademicOnlineWorkBackfillItemId,
    SourceWorkBackfillRunId = i.AcademicOnlineWorkBackfillRunId,
    i.FacultyId,
    i.FacultyDisplayName,
    WorkId = w.WorkId,
    WorkOpenAlexId = w.OpenAlexId,
    WorkOpenAlexShortId = w.OpenAlexShortId,
    WorkTitle = COALESCE(w.Title, w.DisplayName),
    WorkDoi = w.Doi,
    w.PublicationYear,
    w.PublicationDate,
    w.WorkType,
    AcceptedDecisionCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyAuthorMatchDecision dc
        WHERE dc.FacultyId = i.FacultyId
          AND dc.DecisionStatus = 'Accepted'
          AND dc.IsCurrent = 1
    ),
    d.FacultyAuthorMatchDecisionId,
    d.AuthorId,
    d.AuthorOpenAlexId,
    d.AuthorDisplayName,
    d.AuthorOrcid,
    wa.WorkAuthorshipId,
    wa.OrdinalPosition,
    wa.IsCorresponding,
    AuthorshipInstitutionCount =
    (
        SELECT COUNT(*)
        FROM openalex.WorkAuthorshipInstitution wai
        WHERE wai.WorkAuthorshipId = wa.WorkAuthorshipId
    ),
    MatchingInstitutionCount =
    (
        SELECT COUNT(*)
        FROM openalex.WorkAuthorshipInstitution wai
        JOIN openalex.Institution inst
            ON wai.InstitutionOpenAlexId = inst.OpenAlexId
        WHERE wai.WorkAuthorshipId = wa.WorkAuthorshipId
    ),
    ExistingCandidateId =
    (
        SELECT TOP (1) pc.FacultyPublicationCandidateId
        FROM openalex.FacultyPublicationCandidate pc
        WHERE pc.FacultyId = i.FacultyId
          AND pc.WorkId = w.WorkId
          AND pc.CandidateSource = 'AcceptedAuthorMatch'
        ORDER BY pc.FacultyPublicationCandidateId
    )
FROM openalex.AcademicOnlineWorkBackfillRun r
JOIN openalex.AcademicOnlineWorkBackfillItem i
    ON r.AcademicOnlineWorkBackfillRunId = i.AcademicOnlineWorkBackfillRunId
JOIN openalex.Work w
    ON i.WorkIdAfter = w.WorkId
OUTER APPLY
(
    SELECT TOP (1)
        decision.FacultyAuthorMatchDecisionId,
        decision.AuthorId,
        decision.AuthorOpenAlexId,
        decision.AuthorDisplayName,
        decision.AuthorOrcid
    FROM openalex.FacultyAuthorMatchDecision decision
    WHERE decision.FacultyId = i.FacultyId
      AND decision.DecisionStatus = 'Accepted'
      AND decision.IsCurrent = 1
    ORDER BY
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM openalex.WorkAuthorship wax
            WHERE wax.WorkId = w.WorkId
              AND
              (
                  wax.AuthorOpenAlexId = decision.AuthorOpenAlexId
                  OR
                  (
                      decision.AuthorOrcid IS NOT NULL
                      AND wax.AuthorOrcid IS NOT NULL
                      AND wax.AuthorOrcid = decision.AuthorOrcid
                  )
              )
        ) THEN 0 ELSE 1 END,
        decision.FacultyAuthorMatchDecisionId DESC
) d
OUTER APPLY
(
    SELECT TOP (1)
        authorship.WorkAuthorshipId,
        authorship.OrdinalPosition,
        authorship.IsCorresponding
    FROM openalex.WorkAuthorship authorship
    WHERE authorship.WorkId = w.WorkId
      AND d.FacultyAuthorMatchDecisionId IS NOT NULL
      AND
      (
          authorship.AuthorOpenAlexId = d.AuthorOpenAlexId
          OR
          (
              d.AuthorOrcid IS NOT NULL
              AND authorship.AuthorOrcid IS NOT NULL
              AND authorship.AuthorOrcid = d.AuthorOrcid
          )
      )
    ORDER BY authorship.OrdinalPosition, authorship.WorkAuthorshipId
) wa
WHERE r.AcademicOnlineWorkBackfillRunId = @SourceRunId
  AND r.RunStatus = 'Completed'
  AND i.FacultyId = @FacultyId
  AND i.ExecutionStatus = 'Inserted'
  AND i.WorkIdAfter IS NOT NULL
ORDER BY i.AcademicOnlineWorkBackfillItemId;";
        command.Parameters.AddWithValue("@SourceRunId", sourceWorkBackfillRunId);
        command.Parameters.AddWithValue("@FacultyId", facultyId);
        command.Parameters.AddWithValue("@MaxWorks", Math.Clamp(maxWorks, 1, 5));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FacultyPublicationCandidatePreviewSourceRow
            {
                SourceBackfillItemId = reader.GetInt64(reader.GetOrdinal("SourceBackfillItemId")),
                SourceWorkBackfillRunId = reader.GetInt64(reader.GetOrdinal("SourceWorkBackfillRunId")),
                FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
                FacultyDisplayName = reader.GetString(reader.GetOrdinal("FacultyDisplayName")),
                WorkId = reader.GetInt64(reader.GetOrdinal("WorkId")),
                WorkOpenAlexId = reader.GetString(reader.GetOrdinal("WorkOpenAlexId")),
                WorkOpenAlexShortId = GetNullableString(reader, "WorkOpenAlexShortId"),
                WorkTitle = GetNullableString(reader, "WorkTitle"),
                WorkDoi = GetNullableString(reader, "WorkDoi"),
                PublicationYear = GetNullableInt32(reader, "PublicationYear"),
                PublicationDate = GetNullableDateTime(reader, "PublicationDate"),
                WorkType = GetNullableString(reader, "WorkType"),
                AcceptedDecisionCount = reader.GetInt32(reader.GetOrdinal("AcceptedDecisionCount")),
                FacultyAuthorMatchDecisionId = GetNullableInt64(reader, "FacultyAuthorMatchDecisionId"),
                AuthorId = GetNullableInt64(reader, "AuthorId"),
                AuthorOpenAlexId = GetNullableString(reader, "AuthorOpenAlexId"),
                AuthorDisplayName = GetNullableString(reader, "AuthorDisplayName"),
                AuthorOrcid = GetNullableString(reader, "AuthorOrcid"),
                WorkAuthorshipId = GetNullableInt64(reader, "WorkAuthorshipId"),
                OrdinalPosition = GetNullableInt32(reader, "OrdinalPosition"),
                IsCorresponding = GetNullableBool(reader, "IsCorresponding"),
                AuthorshipInstitutionCount = GetNullableInt32(reader, "AuthorshipInstitutionCount") ?? 0,
                MatchingInstitutionCount = GetNullableInt32(reader, "MatchingInstitutionCount") ?? 0,
                ExistingCandidateId = GetNullableInt64(reader, "ExistingCandidateId")
            });
        }

        return rows;
    }

    public async Task SaveItemAsync(
        long previewRunId,
        FacultyPublicationCandidatePreviewSourceRow row,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO openalex.FacultyPublicationCandidatePreviewItem
(
    FacultyPublicationCandidatePreviewRunId,
    SourceBackfillItemId,
    SourceWorkBackfillRunId,
    FacultyId,
    FacultyDisplayName,
    WorkId,
    WorkOpenAlexId,
    WorkOpenAlexShortId,
    WorkTitle,
    WorkDoi,
    PublicationYear,
    PublicationDate,
    WorkType,
    AcceptedDecisionCount,
    FacultyAuthorMatchDecisionId,
    AuthorId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    WorkAuthorshipId,
    OrdinalPosition,
    IsCorresponding,
    AuthorshipInstitutionCount,
    MatchingInstitutionCount,
    ExistingCandidateId,
    ProposedCandidateSource,
    ProposedCandidateStatus,
    ProposedMatchScore,
    PreviewStatus,
    EvidenceSummary,
    EvidenceJson,
    CreatedAtUtc
)
VALUES
(
    @PreviewRunId,
    @SourceBackfillItemId,
    @SourceWorkBackfillRunId,
    @FacultyId,
    @FacultyDisplayName,
    @WorkId,
    @WorkOpenAlexId,
    @WorkOpenAlexShortId,
    @WorkTitle,
    @WorkDoi,
    @PublicationYear,
    @PublicationDate,
    @WorkType,
    @AcceptedDecisionCount,
    @FacultyAuthorMatchDecisionId,
    @AuthorId,
    @AuthorOpenAlexId,
    @AuthorDisplayName,
    @AuthorOrcid,
    @WorkAuthorshipId,
    @OrdinalPosition,
    @IsCorresponding,
    @AuthorshipInstitutionCount,
    @MatchingInstitutionCount,
    @ExistingCandidateId,
    'AcceptedAuthorMatch',
    'PendingReview',
    @ProposedMatchScore,
    @PreviewStatus,
    @EvidenceSummary,
    @EvidenceJson,
    SYSUTCDATETIME()
);";
        command.Parameters.AddWithValue("@PreviewRunId", previewRunId);
        command.Parameters.AddWithValue("@SourceBackfillItemId", row.SourceBackfillItemId);
        command.Parameters.AddWithValue("@SourceWorkBackfillRunId", row.SourceWorkBackfillRunId);
        command.Parameters.AddWithValue("@FacultyId", row.FacultyId);
        command.Parameters.AddWithValue("@FacultyDisplayName", row.FacultyDisplayName);
        command.Parameters.AddWithValue("@WorkId", row.WorkId);
        command.Parameters.AddWithValue("@WorkOpenAlexId", row.WorkOpenAlexId);
        AddNullable(command, "@WorkOpenAlexShortId", SqlDbType.VarChar, 32, row.WorkOpenAlexShortId);
        AddNullable(command, "@WorkTitle", SqlDbType.NVarChar, -1, row.WorkTitle);
        AddNullable(command, "@WorkDoi", SqlDbType.NVarChar, 512, row.WorkDoi);
        AddNullable(command, "@PublicationYear", SqlDbType.Int, row.PublicationYear);
        AddNullable(command, "@PublicationDate", SqlDbType.Date, row.PublicationDate);
        AddNullable(command, "@WorkType", SqlDbType.VarChar, 100, row.WorkType);
        command.Parameters.AddWithValue("@AcceptedDecisionCount", row.AcceptedDecisionCount);
        AddNullable(command, "@FacultyAuthorMatchDecisionId", SqlDbType.BigInt, row.FacultyAuthorMatchDecisionId);
        AddNullable(command, "@AuthorId", SqlDbType.BigInt, row.AuthorId);
        AddNullable(command, "@AuthorOpenAlexId", SqlDbType.VarChar, 64, row.AuthorOpenAlexId);
        AddNullable(command, "@AuthorDisplayName", SqlDbType.NVarChar, 512, row.AuthorDisplayName);
        AddNullable(command, "@AuthorOrcid", SqlDbType.NVarChar, 256, row.AuthorOrcid);
        AddNullable(command, "@WorkAuthorshipId", SqlDbType.BigInt, row.WorkAuthorshipId);
        AddNullable(command, "@OrdinalPosition", SqlDbType.Int, row.OrdinalPosition);
        AddNullable(command, "@IsCorresponding", SqlDbType.Bit, row.IsCorresponding);
        command.Parameters.AddWithValue("@AuthorshipInstitutionCount", row.AuthorshipInstitutionCount);
        command.Parameters.AddWithValue("@MatchingInstitutionCount", row.MatchingInstitutionCount);
        AddNullable(command, "@ExistingCandidateId", SqlDbType.BigInt, row.ExistingCandidateId);
        command.Parameters.AddWithValue("@ProposedMatchScore", row.ProposedMatchScore);
        command.Parameters.AddWithValue("@PreviewStatus", row.PreviewStatus);
        command.Parameters.AddWithValue("@EvidenceSummary", row.EvidenceSummary);
        command.Parameters.AddWithValue("@EvidenceJson", row.EvidenceJson);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(
        long previewRunId,
        int selected,
        int ready,
        int existing,
        int noDecision,
        int multipleDecisions,
        int authorNotOnWork,
        BackfillSafetyCounts safetyAfter,
        string summary,
        CancellationToken cancellationToken = default)
    {
        await UpdateRunAsync(previewRunId, "Completed", selected, ready, existing, noDecision, multipleDecisions, authorNotOnWork, safetyAfter, summary, null, cancellationToken);
    }

    public async Task FailRunAsync(long previewRunId, Exception exception, CancellationToken cancellationToken = default)
    {
        await UpdateRunAsync(previewRunId, "Failed", 0, 0, 0, 0, 0, 0, null, null, exception.ToString(), cancellationToken);
    }

    private async Task UpdateRunAsync(
        long previewRunId,
        string status,
        int selected,
        int ready,
        int existing,
        int noDecision,
        int multipleDecisions,
        int authorNotOnWork,
        BackfillSafetyCounts? safetyAfter,
        string? summary,
        string? error,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE openalex.FacultyPublicationCandidatePreviewRun
SET
    SelectedWorkCount = @Selected,
    ReadyToGenerateCount = @Ready,
    ExistingCandidateCount = @Existing,
    NoAcceptedAuthorDecisionCount = @NoDecision,
    MultipleAcceptedAuthorDecisionCount = @MultipleDecisions,
    AcceptedAuthorNotOnWorkCount = @AuthorNotOnWork,
    CandidateRowsAfter = @CandidateRowsAfter,
    DecisionRowsAfter = @DecisionRowsAfter,
    RunStatus = @Status,
    SummaryText = @Summary,
    ErrorText = @Error,
    CompletedAtUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyPublicationCandidatePreviewRunId = @PreviewRunId;";
        command.Parameters.AddWithValue("@PreviewRunId", previewRunId);
        command.Parameters.AddWithValue("@Selected", selected);
        command.Parameters.AddWithValue("@Ready", ready);
        command.Parameters.AddWithValue("@Existing", existing);
        command.Parameters.AddWithValue("@NoDecision", noDecision);
        command.Parameters.AddWithValue("@MultipleDecisions", multipleDecisions);
        command.Parameters.AddWithValue("@AuthorNotOnWork", authorNotOnWork);
        command.Parameters.AddWithValue("@CandidateRowsAfter", safetyAfter is null ? DBNull.Value : safetyAfter.CandidateRows);
        command.Parameters.AddWithValue("@DecisionRowsAfter", safetyAfter is null ? DBNull.Value : safetyAfter.DecisionRows);
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
    WHEN OBJECT_ID(N'openalex.FacultyPublicationCandidatePreviewRun', N'U') IS NOT NULL
     AND OBJECT_ID(N'openalex.FacultyPublicationCandidatePreviewItem', N'U') IS NOT NULL
    THEN 1 ELSE 0 END;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new InvalidOperationException("PATCH017B2 preview tables do not exist. Run sql/374_V3E_PATCH017B2_FacultyPublicationCandidatePreview_SQL2016.sql first.");
        }
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

    private static int? GetNullableInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static bool? GetNullableBool(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
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

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, int? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, bool? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, DateTime? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value.HasValue ? value.Value.Date : DBNull.Value;
    }
}
