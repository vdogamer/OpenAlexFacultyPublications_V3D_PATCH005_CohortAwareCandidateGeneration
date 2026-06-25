using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class ReviewQueueRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public ReviewQueueRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AuthorReviewQueueItem>> GetAuthorQueueAsync(int top = 100, CancellationToken cancellationToken = default)
    {
        var rows = new List<AuthorReviewQueueItem>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyAuthorMatchCandidate", "U", cancellationToken))
        {
            return rows;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 500)})
    c.FacultyAuthorMatchCandidateId,
    f.DisplayName AS FacultyDisplayName,
    f.PrimaryEmail,
    f.DepartmentName,
    a.DisplayName AS AuthorDisplayName,
    a.OpenAlexId AS AuthorOpenAlexId,
    a.Orcid AS AuthorOrcid,
    c.MatchScore,
    c.CandidateStatus,
    c.EvidenceSummary,
    c.CreatedAtUtc
FROM openalex.FacultyAuthorMatchCandidate c
JOIN openalex.Faculty f
    ON c.FacultyId = f.FacultyId
LEFT JOIN openalex.Author a
    ON c.AuthorId = a.AuthorId
WHERE
    (
        c.CandidateStatus IS NULL
        OR c.CandidateStatus IN ('PendingReview', 'NeedsMoreEvidence')
    )
    AND NOT EXISTS
    (
        SELECT 1
        FROM openalex.FacultyAuthorMatchDecision d
        WHERE d.FacultyAuthorMatchCandidateId = c.FacultyAuthorMatchCandidateId
          AND d.IsCurrent = 1
          AND d.DecisionStatus IN ('Accepted', 'Rejected')
    )
ORDER BY c.CreatedAtUtc DESC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AuthorReviewQueueItem
            {
                FacultyAuthorMatchCandidateId = reader.GetInt64(0),
                FacultyDisplayName = reader.GetString(1),
                FacultyEmail = reader.IsDBNull(2) ? null : reader.GetString(2),
                DepartmentName = reader.IsDBNull(3) ? null : reader.GetString(3),
                AuthorDisplayName = reader.IsDBNull(4) ? null : reader.GetString(4),
                AuthorOpenAlexId = reader.IsDBNull(5) ? null : reader.GetString(5),
                AuthorOrcid = reader.IsDBNull(6) ? null : reader.GetString(6),
                MatchScore = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                CandidateStatus = reader.IsDBNull(8) ? null : reader.GetString(8),
                EvidenceSummary = reader.IsDBNull(9) ? null : reader.GetString(9),
                CreatedAtUtc = reader.GetDateTime(10)
            });
        }

        return rows;
    }

    public async Task<IReadOnlyList<PublicationReviewQueueItem>> GetPublicationQueueAsync(int top = 100, CancellationToken cancellationToken = default)
    {
        var rows = new List<PublicationReviewQueueItem>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.vw_FacultyPublicationReviewQueue", "V", cancellationToken))
        {
            return rows;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
;WITH PendingCandidates AS
(
    SELECT
        q.FacultyPublicationCandidateId,
        q.FacultyDisplayName,
        q.PrimaryEmail,
        q.DepartmentName,
        q.WorkTitle,
        q.PublicationYear,
        q.WorkDoi,
        q.DoiUrl,
        q.WorkOpenAlexId,
        q.AuthorDisplayName,
        q.CandidateStatus,
        q.MatchScore,
        q.EvidenceSummary,
        q.CreatedAtUtc,
        pc.FacultyId,
        CandidateGroupKey = COALESCE(CONVERT(NVARCHAR(64), pc.WorkId), q.WorkOpenAlexId, q.WorkTitle),
        DuplicateCandidateCount = COUNT(*) OVER
        (
            PARTITION BY
                pc.FacultyId,
                COALESCE(CONVERT(NVARCHAR(64), pc.WorkId), q.WorkOpenAlexId, q.WorkTitle)
        ),
        ReviewGroupRank = ROW_NUMBER() OVER
        (
            PARTITION BY
                pc.FacultyId,
                COALESCE(CONVERT(NVARCHAR(64), pc.WorkId), q.WorkOpenAlexId, q.WorkTitle)
            ORDER BY
                ISNULL(q.MatchScore, 0) DESC,
                q.CreatedAtUtc DESC,
                q.FacultyPublicationCandidateId DESC
        )
    FROM openalex.vw_FacultyPublicationReviewQueue q
    JOIN openalex.FacultyPublicationCandidate pc
        ON q.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
    WHERE
        (
            q.CandidateStatus IS NULL
            OR q.CandidateStatus IN ('PendingReview', 'NeedsMoreEvidence')
        )
        AND NOT EXISTS
        (
            SELECT 1
            FROM openalex.FacultyPublicationDecision d
            WHERE d.FacultyPublicationCandidateId = q.FacultyPublicationCandidateId
              AND d.IsCurrent = 1
              AND d.DecisionStatus IN ('Accepted', 'Rejected')
        )
)
SELECT TOP ({Math.Clamp(top, 1, 500)})
    FacultyPublicationCandidateId,
    FacultyDisplayName,
    PrimaryEmail,
    DepartmentName,
    WorkTitle,
    PublicationYear,
    WorkDoi,
    DoiUrl,
    WorkOpenAlexId,
    AuthorDisplayName,
    CandidateStatus,
    MatchScore,
    EvidenceSummary = ISNULL(EvidenceSummary, '')
        + '; PATCH010DuplicateCandidateCount=' + CONVERT(NVARCHAR(20), DuplicateCandidateCount)
        + CASE
            WHEN DuplicateCandidateCount > 1
            THEN '; PATCH010ReviewGrouping=Best-scoring candidate shown for this faculty/work pair; hidden duplicate candidate rows=' + CONVERT(NVARCHAR(20), DuplicateCandidateCount - 1)
            ELSE '; PATCH010ReviewGrouping=Single candidate for this faculty/work pair'
          END,
    CreatedAtUtc
FROM PendingCandidates
WHERE ReviewGroupRank = 1
ORDER BY CreatedAtUtc DESC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new PublicationReviewQueueItem
            {
                FacultyPublicationCandidateId = reader.GetInt64(0),
                FacultyDisplayName = reader.GetString(1),
                PrimaryEmail = reader.IsDBNull(2) ? null : reader.GetString(2),
                DepartmentName = reader.IsDBNull(3) ? null : reader.GetString(3),
                WorkTitle = reader.GetString(4),
                PublicationYear = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                WorkDoi = reader.IsDBNull(6) ? null : reader.GetString(6),
                DoiUrl = reader.IsDBNull(7) ? null : reader.GetString(7),
                WorkOpenAlexId = reader.IsDBNull(8) ? null : reader.GetString(8),
                AuthorDisplayName = reader.IsDBNull(9) ? null : reader.GetString(9),
                CandidateStatus = reader.IsDBNull(10) ? null : reader.GetString(10),
                MatchScore = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                EvidenceSummary = reader.IsDBNull(12) ? null : reader.GetString(12),
                CreatedAtUtc = reader.GetDateTime(13)
            });
        }

        return rows;
    }

    private static async Task<bool> ObjectExistsAsync(Microsoft.Data.SqlClient.SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
