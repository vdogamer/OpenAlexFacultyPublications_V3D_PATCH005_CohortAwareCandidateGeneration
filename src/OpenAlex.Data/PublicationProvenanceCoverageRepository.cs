using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class PublicationProvenanceCoverageRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public PublicationProvenanceCoverageRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PublicationProvenanceCoverageRow>> GetRowsAsync(
        int top = 500,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<PublicationProvenanceCoverageRow>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var requiredObjects = new[]
        {
            "openalex.AcademicOnlinePublicationSnapshot",
            "openalex.AcademicOnlineScholarSnapshot",
            "openalex.Faculty",
            "openalex.Work",
            "openalex.FacultyPublicationCandidate",
            "openalex.FacultyPublicationDecision"
        };

        foreach (var objectName in requiredObjects)
        {
            if (!await ObjectExistsAsync(connection, objectName, "U", cancellationToken))
            {
                return rows;
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
;WITH LatestFacultyRun AS
(
    SELECT
        FacultyId,
        AcademicOnlineComparisonRunId = MAX(AcademicOnlineComparisonRunId)
    FROM openalex.AcademicOnlinePublicationSnapshot
    GROUP BY FacultyId
),
AcademicOnlineRanked AS
(
    SELECT
        p.AcademicOnlinePublicationSnapshotId,
        p.AcademicOnlineComparisonRunId,
        p.FacultyId,
        p.AcademicOnlineAuthorId,
        p.AcademicOnlinePublicationId,
        p.Title,
        p.PublicationYear,
        p.Doi,
        p.OpenAlexId,
        p.Verified,
        p.IsVerifiedQuery,
        s.AcademicOnlineName AS AcademicOnlineScholarName,
        DeduplicationRank = ROW_NUMBER() OVER
        (
            PARTITION BY
                p.FacultyId,
                COALESCE
                (
                    NULLIF(UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')), ''),
                    NULLIF(LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')), ''),
                    LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 120)
                        + '|' + ISNULL(CONVERT(VARCHAR(10), p.PublicationYear), '')
                )
            ORDER BY
                p.IsVerifiedQuery DESC,
                p.Verified DESC,
                p.AcademicOnlinePublicationSnapshotId DESC
        )
    FROM openalex.AcademicOnlinePublicationSnapshot p
    JOIN LatestFacultyRun latest
        ON latest.FacultyId = p.FacultyId
       AND latest.AcademicOnlineComparisonRunId = p.AcademicOnlineComparisonRunId
    LEFT JOIN openalex.AcademicOnlineScholarSnapshot s
        ON s.AcademicOnlineComparisonRunId = p.AcademicOnlineComparisonRunId
       AND s.FacultyId = p.FacultyId
       AND
       (
           s.AcademicOnlineAuthorId = p.AcademicOnlineAuthorId
           OR s.AcademicOnlineAuthorId IS NULL
       )
)
SELECT TOP ({Math.Clamp(top, 1, 5000)})
    ao.AcademicOnlinePublicationSnapshotId,
    ao.AcademicOnlineComparisonRunId,
    ao.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    f.DepartmentName,
    ao.AcademicOnlineAuthorId,
    ao.AcademicOnlineScholarName,
    ao.AcademicOnlinePublicationId,
    ao.Title AS PublicationTitle,
    ao.PublicationYear,
    ao.Doi,
    ao.OpenAlexId,
    ao.Verified AS AcademicOnlineVerified,
    ao.IsVerifiedQuery AS AcademicOnlineVerifiedQuery,
    lw.WorkId AS LocalWorkId,
    lw.OpenAlexId AS LocalWorkOpenAlexId,
    lc.FacultyPublicationCandidateId AS LocalCandidateId,
    lc.CandidateStatus AS LocalCandidateStatus,
    lc.MatchScore AS LocalMatchScore,
    ld.DecisionStatus AS LocalDecisionStatus,
    ld.DecisionSource AS LocalDecisionSource,
    ld.ReviewedBy AS LocalReviewedBy,
    ld.ReviewedAtUtc AS LocalReviewedAtUtc,
    MatchMethod = ISNULL(lw.MatchMethod, ISNULL(lc.MatchMethod, 'NoLocalMatch')),
    CoverageStatus = CASE
        WHEN ld.DecisionStatus = 'Accepted' THEN 'AcademicOnlineAcceptedByUs'
        WHEN lc.FacultyPublicationCandidateId IS NOT NULL THEN 'AcademicOnlineCandidateExists_NotAccepted'
        WHEN lw.WorkId IS NOT NULL THEN 'AcademicOnlineWorkExists_NoCandidate'
        ELSE 'AcademicOnlineOnly_NoLocalWork'
    END,
    CoverageExplanation = CASE
        WHEN ld.DecisionStatus = 'Accepted' THEN 'Academic Online contains this publication and UVA has a current accepted publication decision for the same faculty and work.'
        WHEN lc.FacultyPublicationCandidateId IS NOT NULL THEN 'Academic Online contains this publication and UVA has a faculty-publication candidate, but there is no current accepted decision.'
        WHEN lw.WorkId IS NOT NULL THEN 'Academic Online contains this publication and the OpenAlex work exists locally, but UVA has not created a publication candidate for this faculty.'
        ELSE 'Academic Online contains this publication, but the corresponding OpenAlex work is not present in the local database.'
    END
FROM AcademicOnlineRanked ao
JOIN openalex.Faculty f
    ON f.FacultyId = ao.FacultyId
OUTER APPLY
(
    SELECT TOP (1)
        w.WorkId,
        w.OpenAlexId,
        MatchMethod = CASE
            WHEN NULLIF(LTRIM(RTRIM(ao.OpenAlexId)), '') IS NOT NULL
             AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ao.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                 = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                THEN 'ExactOpenAlexIdMatch'
            WHEN NULLIF(LTRIM(RTRIM(ao.Doi)), '') IS NOT NULL
             AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ao.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                 = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', ''))
                THEN 'ExactDoiMatch'
            ELSE 'LikelyTitleYearMatch'
        END
    FROM openalex.Work w
    WHERE
        (
            NULLIF(LTRIM(RTRIM(ao.OpenAlexId)), '') IS NOT NULL
            AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ao.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
        )
        OR
        (
            NULLIF(LTRIM(RTRIM(ao.Doi)), '') IS NOT NULL
            AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ao.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', ''))
        )
        OR
        (
            ao.PublicationYear = w.PublicationYear
            AND LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(ao.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80)
                = LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80)
        )
    ORDER BY
        CASE
            WHEN NULLIF(LTRIM(RTRIM(ao.OpenAlexId)), '') IS NOT NULL
             AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ao.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                 = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.OpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')) THEN 1
            WHEN NULLIF(LTRIM(RTRIM(ao.Doi)), '') IS NOT NULL
             AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ao.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                 = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(w.Doi, ''))), 'https://doi.org/', ''), 'http://doi.org/', '')) THEN 2
            ELSE 3
        END,
        w.WorkId
) lw
OUTER APPLY
(
    SELECT TOP (1)
        pc.FacultyPublicationCandidateId,
        pc.CandidateStatus,
        pc.MatchScore,
        MatchMethod = CASE
            WHEN NULLIF(LTRIM(RTRIM(ao.OpenAlexId)), '') IS NOT NULL
             AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ao.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                 = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                THEN 'ExactOpenAlexIdMatch'
            WHEN NULLIF(LTRIM(RTRIM(ao.Doi)), '') IS NOT NULL
             AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ao.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                 = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', ''))
                THEN 'ExactDoiMatch'
            ELSE 'LikelyTitleYearMatch'
        END
    FROM openalex.FacultyPublicationCandidate pc
    WHERE pc.FacultyId = ao.FacultyId
      AND
      (
          (lw.WorkId IS NOT NULL AND pc.WorkId = lw.WorkId)
          OR
          (
              NULLIF(LTRIM(RTRIM(ao.OpenAlexId)), '') IS NOT NULL
              AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ao.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
                  = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', ''))
          )
          OR
          (
              NULLIF(LTRIM(RTRIM(ao.Doi)), '') IS NOT NULL
              AND LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ao.Doi)), 'https://doi.org/', ''), 'http://doi.org/', ''))
                  = LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkDoi, ''))), 'https://doi.org/', ''), 'http://doi.org/', ''))
          )
          OR
          (
              ao.PublicationYear = pc.PublicationYear
              AND LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(ao.Title, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80)
                  = LEFT(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(pc.WorkTitle, ''))), ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', '')), 80)
          )
      )
    ORDER BY
        CASE WHEN lw.WorkId IS NOT NULL AND pc.WorkId = lw.WorkId THEN 1 ELSE 2 END,
        pc.MatchScore DESC,
        pc.FacultyPublicationCandidateId
) lc
OUTER APPLY
(
    SELECT TOP (1)
        d.DecisionStatus,
        d.DecisionSource,
        d.ReviewedBy,
        d.ReviewedAtUtc
    FROM openalex.FacultyPublicationDecision d
    WHERE d.FacultyPublicationCandidateId = lc.FacultyPublicationCandidateId
      AND d.IsCurrent = 1
    ORDER BY d.ReviewedAtUtc DESC, d.FacultyPublicationDecisionId DESC
) ld
WHERE ao.DeduplicationRank = 1
ORDER BY
    f.DisplayName,
    ao.PublicationYear DESC,
    ao.Title;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new PublicationProvenanceCoverageRow
            {
                AcademicOnlinePublicationSnapshotId = reader.GetInt64(reader.GetOrdinal("AcademicOnlinePublicationSnapshotId")),
                AcademicOnlineComparisonRunId = reader.GetInt64(reader.GetOrdinal("AcademicOnlineComparisonRunId")),
                FacultyId = reader.GetInt64(reader.GetOrdinal("FacultyId")),
                FacultyDisplayName = reader.GetString(reader.GetOrdinal("FacultyDisplayName")),
                DepartmentName = GetNullableString(reader, "DepartmentName"),
                AcademicOnlineAuthorId = GetNullableString(reader, "AcademicOnlineAuthorId"),
                AcademicOnlineScholarName = GetNullableString(reader, "AcademicOnlineScholarName"),
                AcademicOnlinePublicationId = GetNullableString(reader, "AcademicOnlinePublicationId"),
                PublicationTitle = GetNullableString(reader, "PublicationTitle") ?? string.Empty,
                PublicationYear = GetNullableInt32(reader, "PublicationYear"),
                Doi = GetNullableString(reader, "Doi"),
                OpenAlexId = GetNullableString(reader, "OpenAlexId"),
                AcademicOnlineVerified = GetNullableBool(reader, "AcademicOnlineVerified"),
                AcademicOnlineVerifiedQuery = reader.GetBoolean(reader.GetOrdinal("AcademicOnlineVerifiedQuery")),
                LocalWorkId = GetNullableInt64(reader, "LocalWorkId"),
                LocalWorkOpenAlexId = GetNullableString(reader, "LocalWorkOpenAlexId"),
                LocalCandidateId = GetNullableInt64(reader, "LocalCandidateId"),
                LocalCandidateStatus = GetNullableString(reader, "LocalCandidateStatus"),
                LocalMatchScore = GetNullableDecimal(reader, "LocalMatchScore"),
                LocalDecisionStatus = GetNullableString(reader, "LocalDecisionStatus"),
                LocalDecisionSource = GetNullableString(reader, "LocalDecisionSource"),
                LocalReviewedBy = GetNullableString(reader, "LocalReviewedBy"),
                LocalReviewedAtUtc = GetNullableDateTime(reader, "LocalReviewedAtUtc"),
                MatchMethod = reader.GetString(reader.GetOrdinal("MatchMethod")),
                CoverageStatus = reader.GetString(reader.GetOrdinal("CoverageStatus")),
                CoverageExplanation = reader.GetString(reader.GetOrdinal("CoverageExplanation"))
            });
        }

        return rows;
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
