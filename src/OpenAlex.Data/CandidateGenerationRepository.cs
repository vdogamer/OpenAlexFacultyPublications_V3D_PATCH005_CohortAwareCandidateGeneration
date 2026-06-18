using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class CandidateGenerationRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public CandidateGenerationRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IngestionRunStartResult> StartRunAsync(
        string runMode,
        string institutionFilter,
        int? maxPages,
        int? maxRecords,
        string applicationVersion,
        string startedBy,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
DECLARE @Inserted TABLE
(
    IngestionRunId BIGINT,
    RunCorrelationId UNIQUEIDENTIFIER
);

INSERT INTO openalex.IngestionRun
(
    RunMode,
    Status,
    InstitutionFilter,
    ClearBeforeRun,
    MaxPagesPerEntity,
    MaxRecordsPerEntity,
    StartedBy,
    MachineName,
    ApplicationVersion,
    CreatedAtUtc,
    UpdatedAtUtc
)
OUTPUT INSERTED.IngestionRunId, INSERTED.RunCorrelationId
INTO @Inserted
VALUES
(
    @RunMode,
    'Started',
    @InstitutionFilter,
    0,
    @MaxPages,
    @MaxRecords,
    @StartedBy,
    @MachineName,
    @ApplicationVersion,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);

SELECT IngestionRunId, RunCorrelationId FROM @Inserted;";

        command.Parameters.AddWithValue("@RunMode", runMode);
        command.Parameters.AddWithValue("@InstitutionFilter", institutionFilter);
        command.Parameters.AddWithValue("@MaxPages", (object?)maxPages ?? DBNull.Value);
        command.Parameters.AddWithValue("@MaxRecords", (object?)maxRecords ?? DBNull.Value);
        command.Parameters.AddWithValue("@StartedBy", startedBy);
        command.Parameters.AddWithValue("@MachineName", Environment.MachineName);
        command.Parameters.AddWithValue("@ApplicationVersion", applicationVersion);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Failed to create openalex.IngestionRun row.");
        }

        return new IngestionRunStartResult
        {
            IngestionRunId = reader.GetInt64(0),
            RunCorrelationId = reader.GetGuid(1)
        };
    }

    public async Task CompleteRunAsync(
        long ingestionRunId,
        string summaryText,
        int authorsProcessed = 0,
        int worksProcessed = 0,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
DECLARE @FinishedAtUtc DATETIME2(7) = SYSUTCDATETIME();

UPDATE openalex.IngestionRun
SET
    Status = 'Completed',
    FinishedAtUtc = @FinishedAtUtc,
    ElapsedMs = DATEDIFF_BIG(MILLISECOND, StartedAtUtc, @FinishedAtUtc),
    UpdatedAtUtc = @FinishedAtUtc
WHERE IngestionRunId = @IngestionRunId;

INSERT INTO openalex.IngestionRunSummary
(
    IngestionRunId,
    TotalRequests,
    SuccessfulRequests,
    FailedRequests,
    TotalRetries,
    AuthorsProcessed,
    WorksProcessed,
    SummaryText,
    CreatedAtUtc
)
VALUES
(
    @IngestionRunId,
    0,
    0,
    0,
    0,
    @AuthorsProcessed,
    @WorksProcessed,
    @SummaryText,
    SYSUTCDATETIME()
);";

        command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);
        command.Parameters.AddWithValue("@AuthorsProcessed", authorsProcessed);
        command.Parameters.AddWithValue("@WorksProcessed", worksProcessed);
        command.Parameters.AddWithValue("@SummaryText", summaryText);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FailRunAsync(long ingestionRunId, Exception exception, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
DECLARE @FinishedAtUtc DATETIME2(7) = SYSUTCDATETIME();

UPDATE openalex.IngestionRun
SET
    Status = 'Failed',
    FinishedAtUtc = @FinishedAtUtc,
    ElapsedMs = DATEDIFF_BIG(MILLISECOND, StartedAtUtc, @FinishedAtUtc),
    ErrorCount = ErrorCount + 1,
    UpdatedAtUtc = @FinishedAtUtc
WHERE IngestionRunId = @IngestionRunId;

INSERT INTO openalex.IngestionRunSummary
(
    IngestionRunId,
    FailedRequests,
    SummaryText,
    CreatedAtUtc
)
VALUES
(
    @IngestionRunId,
    1,
    @SummaryText,
    SYSUTCDATETIME()
);";

        command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);
        command.Parameters.AddWithValue("@SummaryText", exception.Message);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FacultyCandidateSource>> LoadFacultyAsync(int top, CancellationToken cancellationToken = default)
    {
        var rows = new List<FacultyCandidateSource>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP (@Top)
    FacultyId,
    DisplayName,
    FirstName,
    LastName,
    Orcid,
    ClaimedOpenAlexAuthorId,
    DepartmentName,
    AppointmentTitle
FROM openalex.Faculty
WHERE IsActive = 1
ORDER BY DisplayName;";
        command.Parameters.AddWithValue("@Top", top);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FacultyCandidateSource
            {
                FacultyId = reader.GetInt64(0),
                DisplayName = reader.GetString(1),
                FirstName = reader.IsDBNull(2) ? null : reader.GetString(2),
                LastName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Orcid = reader.IsDBNull(4) ? null : reader.GetString(4),
                ClaimedOpenAlexAuthorId = reader.IsDBNull(5) ? null : reader.GetString(5),
                DepartmentName = reader.IsDBNull(6) ? null : reader.GetString(6),
                AppointmentTitle = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }

        return rows;
    }


    public async Task<IReadOnlyList<FacultyCandidateSource>> LoadFacultyForPilotCohortAsync(
        string pilotCohortName,
        int top,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pilotCohortName))
        {
            return await LoadFacultyAsync(top, cancellationToken);
        }

        var rows = new List<FacultyCandidateSource>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.PilotFacultyCohort", "U", cancellationToken))
        {
            throw new InvalidOperationException("PilotCohortName was provided, but openalex.PilotFacultyCohort does not exist. Run sql/190_PilotExpansion_Create_Cohort_Table.sql first.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP (@Top)
    f.FacultyId,
    f.DisplayName,
    f.FirstName,
    f.LastName,
    f.Orcid,
    f.ClaimedOpenAlexAuthorId,
    f.DepartmentName,
    f.AppointmentTitle
FROM openalex.PilotFacultyCohort c
JOIN openalex.Faculty f
    ON c.FacultyId = f.FacultyId
WHERE c.CohortName = @PilotCohortName
  AND c.IsEnabled = 1
  AND f.IsActive = 1
ORDER BY c.PilotOrder, f.DisplayName;";
        command.Parameters.AddWithValue("@Top", top);
        command.Parameters.AddWithValue("@PilotCohortName", pilotCohortName.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FacultyCandidateSource
            {
                FacultyId = reader.GetInt64(0),
                DisplayName = reader.GetString(1),
                FirstName = reader.IsDBNull(2) ? null : reader.GetString(2),
                LastName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Orcid = reader.IsDBNull(4) ? null : reader.GetString(4),
                ClaimedOpenAlexAuthorId = reader.IsDBNull(5) ? null : reader.GetString(5),
                DepartmentName = reader.IsDBNull(6) ? null : reader.GetString(6),
                AppointmentTitle = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException($"PilotCohortName '{pilotCohortName}' did not return any active faculty. Verify openalex.PilotFacultyCohort.CohortName and IsEnabled values.");
        }

        return rows;
    }

    public async Task<IReadOnlyList<AuthorCandidateSource>> LoadAuthorsAsync(int top, CancellationToken cancellationToken = default)
    {
        var rows = new List<AuthorCandidateSource>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP (@Top)
    AuthorId,
    OpenAlexId,
    OpenAlexShortId,
    DisplayName,
    Orcid,
    WorksCount,
    CitedByCount
FROM openalex.Author
ORDER BY LastSeenAtUtc DESC, WorksCount DESC, AuthorId DESC;";
        command.Parameters.AddWithValue("@Top", top);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AuthorCandidateSource
            {
                AuthorId = reader.GetInt64(0),
                OpenAlexId = reader.GetString(1),
                OpenAlexShortId = reader.IsDBNull(2) ? null : reader.GetString(2),
                DisplayName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Orcid = reader.IsDBNull(4) ? null : reader.GetString(4),
                WorksCount = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                CitedByCount = reader.IsDBNull(6) ? null : reader.GetInt32(6)
            });
        }

        return rows;
    }

    public async Task<IReadOnlyDictionary<long, List<AuthorInstitutionEvidence>>> LoadAuthorInstitutionsAsync(
        IReadOnlyCollection<long> authorIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<long, List<AuthorInstitutionEvidence>>();

        if (authorIds.Count == 0)
        {
            return result;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var parameterNames = authorIds.Select((_, i) => "@A" + i).ToArray();

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT
    AuthorId,
    InstitutionOpenAlexId,
    InstitutionDisplayName,
    InstitutionRor,
    CountryCode,
    InstitutionType
FROM openalex.AuthorLastKnownInstitution
WHERE AuthorId IN ({string.Join(",", parameterNames)})
ORDER BY AuthorId, OrdinalPosition;";

        var index = 0;
        foreach (var authorId in authorIds)
        {
            command.Parameters.AddWithValue(parameterNames[index], authorId);
            index++;
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = new AuthorInstitutionEvidence
            {
                AuthorId = reader.GetInt64(0),
                InstitutionOpenAlexId = reader.IsDBNull(1) ? null : reader.GetString(1),
                InstitutionDisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                InstitutionRor = reader.IsDBNull(3) ? null : reader.GetString(3),
                CountryCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                InstitutionType = reader.IsDBNull(5) ? null : reader.GetString(5)
            };

            if (!result.TryGetValue(item.AuthorId, out var list))
            {
                list = new List<AuthorInstitutionEvidence>();
                result[item.AuthorId] = list;
            }

            list.Add(item);
        }

        return result;
    }

    public async Task<bool> UpsertAuthorCandidateAsync(AuthorCandidateWriteModel candidate, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var update = connection.CreateCommand();
        update.CommandText = @"
UPDATE openalex.FacultyAuthorMatchCandidate
SET
    AuthorId = @AuthorId,
    AuthorOpenAlexShortId = @AuthorOpenAlexShortId,
    AuthorDisplayName = @AuthorDisplayName,
    AuthorOrcid = @AuthorOrcid,
    CandidateStatus = @CandidateStatus,
    MatchScore = @MatchScore,
    NameScore = @NameScore,
    OrcidScore = @OrcidScore,
    InstitutionScore = @InstitutionScore,
    CoauthorScore = @CoauthorScore,
    PublicationScore = @PublicationScore,
    EvidenceSummary = @EvidenceSummary,
    EvidenceJson = @EvidenceJson,
    IngestionRunId = @IngestionRunId,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyId = @FacultyId
  AND AuthorOpenAlexId = @AuthorOpenAlexId
  AND CandidateSource = @CandidateSource;";
        AddAuthorCandidateParameters(update, candidate);

        var updatedRows = await update.ExecuteNonQueryAsync(cancellationToken);
        if (updatedRows > 0)
        {
            return false;
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = @"
INSERT INTO openalex.FacultyAuthorMatchCandidate
(
    FacultyId,
    AuthorId,
    AuthorOpenAlexId,
    AuthorOpenAlexShortId,
    AuthorDisplayName,
    AuthorOrcid,
    CandidateSource,
    CandidateStatus,
    MatchScore,
    NameScore,
    OrcidScore,
    InstitutionScore,
    CoauthorScore,
    PublicationScore,
    EvidenceSummary,
    EvidenceJson,
    IngestionRunId,
    CreatedAtUtc,
    UpdatedAtUtc
)
VALUES
(
    @FacultyId,
    @AuthorId,
    @AuthorOpenAlexId,
    @AuthorOpenAlexShortId,
    @AuthorDisplayName,
    @AuthorOrcid,
    @CandidateSource,
    @CandidateStatus,
    @MatchScore,
    @NameScore,
    @OrcidScore,
    @InstitutionScore,
    @CoauthorScore,
    @PublicationScore,
    @EvidenceSummary,
    @EvidenceJson,
    @IngestionRunId,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        AddAuthorCandidateParameters(insert, candidate);
        await insert.ExecuteNonQueryAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<PublicationCandidateSourceRow>> GetPublicationCandidateSourceRowsAsync(
        int maxRows,
        bool onlyCurrentAcceptedAuthorDecisions,
        string? pilotCohortName = null,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<PublicationCandidateSourceRow>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var onlyCurrentClause = onlyCurrentAcceptedAuthorDecisions ? "AND d.IsCurrent = 1" : string.Empty;
        var cohortClause = !string.IsNullOrWhiteSpace(pilotCohortName)
            ? @"AND EXISTS
    (
        SELECT 1
        FROM openalex.PilotFacultyCohort pc
        WHERE pc.FacultyId = d.FacultyId
          AND pc.CohortName = @PilotCohortName
          AND pc.IsEnabled = 1
    )"
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(pilotCohortName) && !await ObjectExistsAsync(connection, "openalex.PilotFacultyCohort", "U", cancellationToken))
        {
            throw new InvalidOperationException("PilotCohortName was provided, but openalex.PilotFacultyCohort does not exist. Run sql/190_PilotExpansion_Create_Cohort_Table.sql first.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT TOP (@MaxRows)
    d.FacultyAuthorMatchDecisionId,
    d.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    d.AuthorId,
    d.AuthorOpenAlexId,
    d.AuthorDisplayName,
    d.AuthorOrcid,
    w.WorkId,
    w.OpenAlexId AS WorkOpenAlexId,
    w.OpenAlexShortId AS WorkOpenAlexShortId,
    COALESCE(w.Title, w.DisplayName) AS WorkTitle,
    w.Doi AS WorkDoi,
    w.PublicationYear,
    w.PublicationDate,
    w.WorkType,
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
        JOIN openalex.Institution i
            ON wai.InstitutionOpenAlexId = i.OpenAlexId
        WHERE wai.WorkAuthorshipId = wa.WorkAuthorshipId
    ),
    ExistingCandidateId =
    (
        SELECT TOP 1 pc.FacultyPublicationCandidateId
        FROM openalex.FacultyPublicationCandidate pc
        WHERE pc.FacultyId = d.FacultyId
          AND pc.WorkId = w.WorkId
          AND pc.CandidateSource = 'AcceptedAuthorMatch'
    )
FROM openalex.FacultyAuthorMatchDecision d
JOIN openalex.Faculty f
    ON d.FacultyId = f.FacultyId
JOIN openalex.WorkAuthorship wa
    ON
    (
        wa.AuthorOpenAlexId = d.AuthorOpenAlexId
        OR
        (
            d.AuthorOrcid IS NOT NULL
            AND wa.AuthorOrcid IS NOT NULL
            AND wa.AuthorOrcid = d.AuthorOrcid
        )
    )
JOIN openalex.Work w
    ON wa.WorkId = w.WorkId
WHERE d.DecisionStatus = 'Accepted'
  {onlyCurrentClause}
  {cohortClause}
ORDER BY
    f.DisplayName,
    w.PublicationYear DESC,
    w.WorkId DESC;";
        command.Parameters.AddWithValue("@MaxRows", maxRows);
        if (!string.IsNullOrWhiteSpace(pilotCohortName))
        {
            command.Parameters.AddWithValue("@PilotCohortName", pilotCohortName.Trim());
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new PublicationCandidateSourceRow
            {
                FacultyAuthorMatchDecisionId = reader.GetInt64(reader.GetOrdinal("FacultyAuthorMatchDecisionId")),
                FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
                FacultyDisplayName = reader.GetString(reader.GetOrdinal("FacultyDisplayName")),
                AuthorId = GetNullableInt64(reader, "AuthorId"),
                AuthorOpenAlexId = reader.GetString(reader.GetOrdinal("AuthorOpenAlexId")),
                AuthorDisplayName = GetNullableString(reader, "AuthorDisplayName"),
                AuthorOrcid = GetNullableString(reader, "AuthorOrcid"),
                WorkId = reader.GetInt64(reader.GetOrdinal("WorkId")),
                WorkOpenAlexId = reader.GetString(reader.GetOrdinal("WorkOpenAlexId")),
                WorkOpenAlexShortId = GetNullableString(reader, "WorkOpenAlexShortId"),
                WorkTitle = GetNullableString(reader, "WorkTitle"),
                WorkDoi = GetNullableString(reader, "WorkDoi"),
                PublicationYear = GetNullableInt32(reader, "PublicationYear"),
                PublicationDate = GetNullableDateTime(reader, "PublicationDate"),
                WorkType = GetNullableString(reader, "WorkType"),
                WorkAuthorshipId = reader.GetInt64(reader.GetOrdinal("WorkAuthorshipId")),
                OrdinalPosition = GetNullableInt32(reader, "OrdinalPosition"),
                IsCorresponding = GetNullableBool(reader, "IsCorresponding"),
                AuthorshipInstitutionCount = GetNullableInt32(reader, "AuthorshipInstitutionCount") ?? 0,
                MatchingInstitutionCount = GetNullableInt32(reader, "MatchingInstitutionCount") ?? 0,
                ExistingCandidateId = GetNullableInt64(reader, "ExistingCandidateId")
            };

            row.MatchScore = ComputePublicationScore(row);
            row.EvidenceSummary = BuildPublicationEvidenceSummary(row);
            row.EvidenceJson = JsonSerializer.Serialize(new
            {
                row.FacultyAuthorMatchDecisionId,
                row.FacultyId,
                row.FacultyDisplayName,
                row.AuthorOpenAlexId,
                row.AuthorDisplayName,
                row.WorkId,
                row.WorkOpenAlexId,
                row.WorkTitle,
                row.PublicationYear,
                row.WorkAuthorshipId,
                row.OrdinalPosition,
                row.IsCorresponding,
                row.AuthorshipInstitutionCount,
                row.MatchingInstitutionCount,
                row.MatchScore
            });

            rows.Add(row);
        }

        return rows;
    }

    public async Task<bool> UpsertPublicationCandidateAsync(
        PublicationCandidateSourceRow row,
        long ingestionRunId,
        string candidateStatus,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (row.ExistingCandidateId.HasValue)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = @"
UPDATE openalex.FacultyPublicationCandidate
SET
    WorkOpenAlexId = @WorkOpenAlexId,
    WorkOpenAlexShortId = @WorkOpenAlexShortId,
    WorkTitle = @WorkTitle,
    WorkDoi = @WorkDoi,
    PublicationYear = @PublicationYear,
    PublicationDate = @PublicationDate,
    WorkType = @WorkType,
    FacultyAuthorMatchDecisionId = @FacultyAuthorMatchDecisionId,
    AuthorOpenAlexId = @AuthorOpenAlexId,
    AuthorDisplayName = @AuthorDisplayName,
    AuthorOrcid = @AuthorOrcid,
    CandidateStatus = @CandidateStatus,
    MatchScore = @MatchScore,
    EvidenceSummary = @EvidenceSummary,
    EvidenceJson = @EvidenceJson,
    IngestionRunId = @IngestionRunId,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyPublicationCandidateId = @FacultyPublicationCandidateId;";
            update.Parameters.AddWithValue("@FacultyPublicationCandidateId", row.ExistingCandidateId.Value);
            AddPublicationCandidateParameters(update, row, ingestionRunId, candidateStatus);
            await update.ExecuteNonQueryAsync(cancellationToken);
            return false;
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = @"
INSERT INTO openalex.FacultyPublicationCandidate
(
    FacultyId,
    WorkId,
    WorkOpenAlexId,
    WorkOpenAlexShortId,
    WorkTitle,
    WorkDoi,
    PublicationYear,
    PublicationDate,
    WorkType,
    FacultyAuthorMatchDecisionId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    CandidateSource,
    CandidateStatus,
    MatchScore,
    EvidenceSummary,
    EvidenceJson,
    IngestionRunId,
    CreatedAtUtc,
    UpdatedAtUtc
)
VALUES
(
    @FacultyId,
    @WorkId,
    @WorkOpenAlexId,
    @WorkOpenAlexShortId,
    @WorkTitle,
    @WorkDoi,
    @PublicationYear,
    @PublicationDate,
    @WorkType,
    @FacultyAuthorMatchDecisionId,
    @AuthorOpenAlexId,
    @AuthorDisplayName,
    @AuthorOrcid,
    'AcceptedAuthorMatch',
    @CandidateStatus,
    @MatchScore,
    @EvidenceSummary,
    @EvidenceJson,
    @IngestionRunId,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";
        AddPublicationCandidateParameters(insert, row, ingestionRunId, candidateStatus);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    private static void AddAuthorCandidateParameters(SqlCommand command, AuthorCandidateWriteModel candidate)
    {
        command.Parameters.AddWithValue("@FacultyId", candidate.FacultyId);
        AddNullable(command, "@AuthorId", SqlDbType.BigInt, candidate.AuthorId);
        command.Parameters.AddWithValue("@AuthorOpenAlexId", candidate.AuthorOpenAlexId);
        AddNullable(command, "@AuthorOpenAlexShortId", SqlDbType.VarChar, 32, candidate.AuthorOpenAlexShortId);
        AddNullable(command, "@AuthorDisplayName", SqlDbType.NVarChar, 512, candidate.AuthorDisplayName);
        AddNullable(command, "@AuthorOrcid", SqlDbType.NVarChar, 256, candidate.AuthorOrcid);
        command.Parameters.AddWithValue("@CandidateSource", candidate.CandidateSource);
        command.Parameters.AddWithValue("@CandidateStatus", candidate.CandidateStatus);
        command.Parameters.AddWithValue("@MatchScore", candidate.MatchScore);
        command.Parameters.AddWithValue("@NameScore", candidate.NameScore);
        command.Parameters.AddWithValue("@OrcidScore", candidate.OrcidScore);
        command.Parameters.AddWithValue("@InstitutionScore", candidate.InstitutionScore);
        command.Parameters.AddWithValue("@CoauthorScore", candidate.CoauthorScore);
        command.Parameters.AddWithValue("@PublicationScore", candidate.PublicationScore);
        AddNullable(command, "@EvidenceSummary", SqlDbType.NVarChar, -1, candidate.EvidenceSummary);
        AddNullable(command, "@EvidenceJson", SqlDbType.NVarChar, -1, candidate.EvidenceJson);
        command.Parameters.AddWithValue("@IngestionRunId", candidate.IngestionRunId);
    }

    private static void AddPublicationCandidateParameters(
        SqlCommand command,
        PublicationCandidateSourceRow row,
        long ingestionRunId,
        string candidateStatus)
    {
        command.Parameters.AddWithValue("@FacultyId", row.FacultyId);
        command.Parameters.AddWithValue("@WorkId", row.WorkId);
        command.Parameters.AddWithValue("@WorkOpenAlexId", row.WorkOpenAlexId);
        AddNullable(command, "@WorkOpenAlexShortId", SqlDbType.VarChar, 32, row.WorkOpenAlexShortId);
        AddNullable(command, "@WorkTitle", SqlDbType.NVarChar, -1, row.WorkTitle);
        AddNullable(command, "@WorkDoi", SqlDbType.NVarChar, 512, row.WorkDoi);
        AddNullable(command, "@PublicationYear", SqlDbType.Int, row.PublicationYear);
        AddNullable(command, "@PublicationDate", SqlDbType.Date, row.PublicationDate);
        AddNullable(command, "@WorkType", SqlDbType.VarChar, 100, row.WorkType);
        command.Parameters.AddWithValue("@FacultyAuthorMatchDecisionId", row.FacultyAuthorMatchDecisionId);
        AddNullable(command, "@AuthorOpenAlexId", SqlDbType.VarChar, 64, row.AuthorOpenAlexId);
        AddNullable(command, "@AuthorDisplayName", SqlDbType.NVarChar, 512, row.AuthorDisplayName);
        AddNullable(command, "@AuthorOrcid", SqlDbType.NVarChar, 256, row.AuthorOrcid);
        command.Parameters.AddWithValue("@CandidateStatus", candidateStatus);
        command.Parameters.AddWithValue("@MatchScore", row.MatchScore);
        AddNullable(command, "@EvidenceSummary", SqlDbType.NVarChar, -1, row.EvidenceSummary);
        AddNullable(command, "@EvidenceJson", SqlDbType.NVarChar, -1, row.EvidenceJson);
        command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);
    }

    private static decimal ComputePublicationScore(PublicationCandidateSourceRow row)
    {
        var score = 80m;

        if (row.IsCorresponding == true)
        {
            score += 5m;
        }

        if (row.MatchingInstitutionCount > 0)
        {
            score += 10m;
        }

        if (row.AuthorshipInstitutionCount > 0)
        {
            score += 5m;
        }

        return Math.Min(100m, score);
    }

    private static string BuildPublicationEvidenceSummary(PublicationCandidateSourceRow row)
    {
        return $"AcceptedAuthorMatch: Faculty '{row.FacultyDisplayName}' has accepted author match '{row.AuthorDisplayName}' on work '{row.WorkTitle}'. MatchScore={row.MatchScore}; AuthorshipInstitutionCount={row.AuthorshipInstitutionCount}; MatchingInstitutionCount={row.MatchingInstitutionCount}.";
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


    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
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

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, string? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
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

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, DateTime? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value.HasValue ? value.Value.Date : DBNull.Value;
    }
}
