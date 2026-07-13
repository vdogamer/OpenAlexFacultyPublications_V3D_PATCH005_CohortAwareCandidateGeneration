using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class PublicationProvenanceRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public PublicationProvenanceRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PublicationProvenanceDataRow>> GetRowsAsync(
        int top = 500,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<PublicationProvenanceDataRow>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyPublicationCandidate", "U", cancellationToken) ||
            !await ObjectExistsAsync(connection, "openalex.Faculty", "U", cancellationToken))
        {
            return rows;
        }

        var hasAuthorDecision = await ObjectExistsAsync(connection, "openalex.FacultyAuthorMatchDecision", "U", cancellationToken);
        var hasAuthorCandidate = await ObjectExistsAsync(connection, "openalex.FacultyAuthorMatchCandidate", "U", cancellationToken);
        var hasPublicationDecision = await ObjectExistsAsync(connection, "openalex.FacultyPublicationDecision", "U", cancellationToken);
        var hasAcademicOnline =
            await ObjectExistsAsync(connection, "openalex.AcademicOnlinePublicationSnapshot", "U", cancellationToken) &&
            await ObjectExistsAsync(connection, "openalex.AcademicOnlineScholarSnapshot", "U", cancellationToken);

        var authorDecisionJoin = hasAuthorDecision
            ? "LEFT JOIN openalex.FacultyAuthorMatchDecision famd ON pc.FacultyAuthorMatchDecisionId = famd.FacultyAuthorMatchDecisionId"
            : string.Empty;

        var authorCandidateJoin = hasAuthorDecision && hasAuthorCandidate
            ? "LEFT JOIN openalex.FacultyAuthorMatchCandidate famc ON famd.FacultyAuthorMatchCandidateId = famc.FacultyAuthorMatchCandidateId"
            : string.Empty;

        var publicationDecisionJoin = hasPublicationDecision
            ? "LEFT JOIN openalex.FacultyPublicationDecision d ON d.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId AND d.IsCurrent = 1"
            : string.Empty;

        var identityCandidateSourceSelect = hasAuthorDecision && hasAuthorCandidate
            ? "famc.CandidateSource AS IdentityCandidateSource"
            : "CAST(NULL AS VARCHAR(100)) AS IdentityCandidateSource";

        var identityDecisionSelect = hasAuthorDecision
            ? @"famd.DecisionSource AS IdentityDecisionSource,
    famd.DecisionStatus AS IdentityDecisionStatus,
    famd.ReviewedBy AS IdentityReviewedBy,
    famd.ReviewedAtUtc AS IdentityReviewedAtUtc"
            : @"CAST(NULL AS VARCHAR(100)) AS IdentityDecisionSource,
    CAST(NULL AS VARCHAR(100)) AS IdentityDecisionStatus,
    CAST(NULL AS NVARCHAR(256)) AS IdentityReviewedBy,
    CAST(NULL AS DATETIME2) AS IdentityReviewedAtUtc";

        var publicationDecisionSelect = hasPublicationDecision
            ? @"d.DecisionSource AS PublicationDecisionSource,
    d.DecisionStatus AS PublicationDecisionStatus,
    d.ReviewedBy AS PublicationReviewedBy,
    d.ReviewedAtUtc AS PublicationReviewedAtUtc"
            : @"CAST(NULL AS VARCHAR(100)) AS PublicationDecisionSource,
    CAST(NULL AS VARCHAR(100)) AS PublicationDecisionStatus,
    CAST(NULL AS NVARCHAR(256)) AS PublicationReviewedBy,
    CAST(NULL AS DATETIME2) AS PublicationReviewedAtUtc";

        var academicOnlineSelect = hasAcademicOnline
            ? @"CASE WHEN ao.AcademicOnlinePublicationSnapshotId IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS AcademicOnlineObserved,
    ao.Verified AS AcademicOnlineVerified,
    ao.IsVerifiedQuery AS AcademicOnlineVerifiedQuery,
    ao.AcademicOnlineComparisonRunId,
    ao.AcademicOnlineAuthorId,
    ao.AcademicOnlineScholarName,
    ao.MatchMethod AS AcademicOnlineMatchMethod"
            : @"CAST(0 AS BIT) AS AcademicOnlineObserved,
    CAST(NULL AS BIT) AS AcademicOnlineVerified,
    CAST(NULL AS BIT) AS AcademicOnlineVerifiedQuery,
    CAST(NULL AS BIGINT) AS AcademicOnlineComparisonRunId,
    CAST(NULL AS NVARCHAR(128)) AS AcademicOnlineAuthorId,
    CAST(NULL AS NVARCHAR(512)) AS AcademicOnlineScholarName,
    CAST(NULL AS VARCHAR(50)) AS AcademicOnlineMatchMethod";

        var academicOnlineApply = hasAcademicOnline
            ? @"
OUTER APPLY
(
    SELECT TOP (1)
        p.AcademicOnlinePublicationSnapshotId,
        p.Verified,
        p.IsVerifiedQuery,
        p.AcademicOnlineComparisonRunId,
        p.AcademicOnlineAuthorId,
        s.AcademicOnlineName AS AcademicOnlineScholarName,
        MatchMethod = CASE
            WHEN NULLIF(LTRIM(RTRIM(p.OpenAlexId)), '') IS NOT NULL
             AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(p.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                 = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                THEN 'ExactOpenAlexIdMatch'
            WHEN NULLIF(LTRIM(RTRIM(p.Doi)), '') IS NOT NULL
             AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(p.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                 = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', ''))
                THEN 'ExactDoiMatch'
            ELSE 'LikelyTitleYearMatch'
        END
    FROM openalex.AcademicOnlinePublicationSnapshot p
    LEFT JOIN openalex.AcademicOnlineScholarSnapshot s
        ON s.AcademicOnlineComparisonRunId = p.AcademicOnlineComparisonRunId
       AND s.FacultyId = p.FacultyId
       AND
       (
           s.AcademicOnlineAuthorId = p.AcademicOnlineAuthorId
           OR s.AcademicOnlineAuthorId IS NULL
       )
    WHERE p.FacultyId = pc.FacultyId
      AND
      (
          (
              NULLIF(LTRIM(RTRIM(p.OpenAlexId)), '') IS NOT NULL
              AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(p.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                  = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
          )
          OR
          (
              NULLIF(LTRIM(RTRIM(p.Doi)), '') IS NOT NULL
              AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(p.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                  = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', ''))
          )
          OR
          (
              p.PublicationYear = pc.PublicationYear
              AND LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80)
                  = LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkTitle, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80)
          )
      )
    ORDER BY
        p.AcademicOnlineComparisonRunId DESC,
        p.IsVerifiedQuery DESC,
        p.AcademicOnlinePublicationSnapshotId DESC
) ao"
            : string.Empty;

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 5000)})
    pc.FacultyPublicationCandidateId,
    pc.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    f.DepartmentName,
    pc.WorkId,
    pc.WorkOpenAlexId,
    pc.WorkTitle,
    pc.WorkDoi,
    pc.PublicationYear,
    pc.AuthorDisplayName,
    pc.AuthorOpenAlexId,
    pc.MatchScore,
    pc.CandidateSource AS PublicationCandidateSource,
    pc.CandidateStatus AS PublicationCandidateStatus,
    {identityCandidateSourceSelect},
    {identityDecisionSelect},
    {publicationDecisionSelect},
    {academicOnlineSelect}
FROM openalex.FacultyPublicationCandidate pc
JOIN openalex.Faculty f
    ON pc.FacultyId = f.FacultyId
{authorDecisionJoin}
{authorCandidateJoin}
{publicationDecisionJoin}
{academicOnlineApply}
ORDER BY
    f.DisplayName,
    pc.PublicationYear DESC,
    pc.WorkTitle;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new PublicationProvenanceDataRow
            {
                FacultyPublicationCandidateId = reader.GetInt64(reader.GetOrdinal("FacultyPublicationCandidateId")),
                FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
                FacultyDisplayName = reader.GetString(reader.GetOrdinal("FacultyDisplayName")),
                DepartmentName = GetNullableString(reader, "DepartmentName"),
                WorkId = GetNullableInt64(reader, "WorkId"),
                WorkOpenAlexId = GetNullableString(reader, "WorkOpenAlexId"),
                WorkTitle = GetNullableString(reader, "WorkTitle") ?? string.Empty,
                WorkDoi = GetNullableString(reader, "WorkDoi"),
                PublicationYear = GetNullableInt32(reader, "PublicationYear"),
                AuthorDisplayName = GetNullableString(reader, "AuthorDisplayName"),
                AuthorOpenAlexId = GetNullableString(reader, "AuthorOpenAlexId"),
                MatchScore = GetNullableDecimal(reader, "MatchScore"),
                PublicationCandidateSource = GetNullableString(reader, "PublicationCandidateSource"),
                PublicationCandidateStatus = GetNullableString(reader, "PublicationCandidateStatus"),
                IdentityCandidateSource = GetNullableString(reader, "IdentityCandidateSource"),
                IdentityDecisionSource = GetNullableString(reader, "IdentityDecisionSource"),
                IdentityDecisionStatus = GetNullableString(reader, "IdentityDecisionStatus"),
                IdentityReviewedBy = GetNullableString(reader, "IdentityReviewedBy"),
                IdentityReviewedAtUtc = GetNullableDateTime(reader, "IdentityReviewedAtUtc"),
                PublicationDecisionSource = GetNullableString(reader, "PublicationDecisionSource"),
                PublicationDecisionStatus = GetNullableString(reader, "PublicationDecisionStatus"),
                PublicationReviewedBy = GetNullableString(reader, "PublicationReviewedBy"),
                PublicationReviewedAtUtc = GetNullableDateTime(reader, "PublicationReviewedAtUtc"),
                AcademicOnlineObserved = reader.GetBoolean(reader.GetOrdinal("AcademicOnlineObserved")),
                AcademicOnlineVerified = GetNullableBool(reader, "AcademicOnlineVerified"),
                AcademicOnlineVerifiedQuery = GetNullableBool(reader, "AcademicOnlineVerifiedQuery"),
                AcademicOnlineComparisonRunId = GetNullableInt64(reader, "AcademicOnlineComparisonRunId"),
                AcademicOnlineAuthorId = GetNullableString(reader, "AcademicOnlineAuthorId"),
                AcademicOnlineScholarName = GetNullableString(reader, "AcademicOnlineScholarName"),
                AcademicOnlineMatchMethod = GetNullableString(reader, "AcademicOnlineMatchMethod")
            };

            row.MetadataSource = row.AcademicOnlineObserved ? "OpenAlex + Academic Online observation" : "OpenAlex";
            row.ResponsibilitySummary = BuildResponsibilitySummary(row);
            rows.Add(row);
        }

        return rows;
    }

    private static string BuildResponsibilitySummary(PublicationProvenanceDataRow row)
    {
        var identityRole = FirstValue(row.IdentityDecisionSource, row.IdentityCandidateSource, "Unknown");
        var publicationMatchRole = FirstValue(row.PublicationCandidateSource, "Unknown");
        var verificationRole = string.IsNullOrWhiteSpace(row.PublicationDecisionStatus)
            ? "UVA publication review pending"
            : $"{FirstValue(row.PublicationDecisionSource, "UVA review")} ({row.PublicationDecisionStatus})";

        var academicOnlineRole = !row.AcademicOnlineObserved
            ? "Academic Online not observed in current snapshots"
            : row.AcademicOnlineVerified == true
                ? "Academic Online observed and marked verified"
                : "Academic Online observed; verification not confirmed";

        return $"Metadata={row.MetadataSource}; IdentityLink={identityRole}; PublicationMatch={publicationMatchRole}; Verification={verificationRole}; AcademicOnline={academicOnlineRole}.";
    }

    private static string FirstValue(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Unknown";
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

    private static decimal? GetNullableDecimal(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    private static bool? GetNullableBool(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    private static async Task<bool> ObjectExistsAsync(
        SqlConnection connection,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
