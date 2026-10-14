using Microsoft.Data.SqlClient;
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

    public async Task<IReadOnlyList<PublicationReviewQueueItem>> GetPublicationQueueAsync(
        int top = 100,
        string? searchText = null,
        decimal? minimumScore = null,
        string institutionEvidenceFilter = "Any",
        string coauthorEvidenceFilter = "Any",
        string nearDuplicateFilter = "Any",
        string resolutionStatusFilter = "Any",
        CancellationToken cancellationToken = default)
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
        pc.WorkId,
        CandidateGroupKey = COALESCE(CONVERT(NVARCHAR(64), pc.WorkId), q.WorkOpenAlexId, q.WorkTitle),
        NearDuplicateTitleKey = LEFT(
            LOWER(
                REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    LTRIM(RTRIM(ISNULL(q.WorkTitle, ''))),
                    ':', ''), '.', ''), ',', ''), '-', ''), '(', ''), ')', ''), '/', ''), CHAR(39), ''), '  ', ' ')
            ),
            90
        ),
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
),
VisibleCandidates AS
(
    SELECT *
    FROM PendingCandidates
    WHERE ReviewGroupRank = 1
),
VisibleCandidatesWithNearDuplicates AS
(
    SELECT
        vc.*,
        NearDuplicateCandidateCount = COUNT(*) OVER
        (
            PARTITION BY
                vc.FacultyId,
                ISNULL(vc.PublicationYear, -1),
                ISNULL(vc.AuthorDisplayName, ''),
                vc.NearDuplicateTitleKey
        )
    FROM VisibleCandidates vc
)
SELECT TOP ({Math.Clamp(top, 1, 500)})
    vc.FacultyPublicationCandidateId,
    vc.FacultyDisplayName,
    vc.PrimaryEmail,
    vc.DepartmentName,
    vc.WorkTitle,
    vc.PublicationYear,
    vc.WorkDoi,
    vc.DoiUrl,
    vc.WorkOpenAlexId,
    vc.AuthorDisplayName,
    vc.CandidateStatus,
    vc.MatchScore,
    EvidenceSummary = ISNULL(vc.EvidenceSummary, '')
        + '; PATCH010DuplicateCandidateCount=' + CONVERT(NVARCHAR(20), vc.DuplicateCandidateCount)
        + CASE
            WHEN vc.DuplicateCandidateCount > 1
            THEN '; PATCH010ReviewGrouping=Best-scoring candidate shown for this faculty/work pair; hidden duplicate candidate rows=' + CONVERT(NVARCHAR(20), vc.DuplicateCandidateCount - 1)
            ELSE '; PATCH010ReviewGrouping=Single candidate for this faculty/work pair'
          END
        + '; PATCH011NearDuplicateCandidateCount=' + CONVERT(NVARCHAR(20), vc.NearDuplicateCandidateCount)
        + CASE
            WHEN vc.NearDuplicateCandidateCount > 1 AND res.ResolutionStatus IS NULL
            THEN '; PATCH011NearDuplicateWarning=Possible near-duplicate publication group: similar title, publication year, faculty, and matched author appear across ' + CONVERT(NVARCHAR(20), vc.NearDuplicateCandidateCount) + ' OpenAlex work records'
            ELSE '; PATCH011NearDuplicateWarning='
          END
        + '; PATCH012ResolutionStatus=' + ISNULL(res.ResolutionStatus, '')
        + '; PATCH012RelatedCandidateId=' + ISNULL(CONVERT(NVARCHAR(20), res.RelatedFacultyPublicationCandidateId), '')
        + '; PATCH012ResolutionReason=' + ISNULL(REPLACE(REPLACE(res.ResolutionReason, ';', ','), CHAR(13) + CHAR(10), ' '), ''),
    vc.CreatedAtUtc
FROM VisibleCandidatesWithNearDuplicates vc
LEFT JOIN openalex.FacultyPublicationNearDuplicateResolution res
    ON res.FacultyPublicationCandidateId = vc.FacultyPublicationCandidateId
   AND res.IsCurrent = 1
WHERE
    (
        @SearchTextLike IS NULL
        OR vc.FacultyDisplayName LIKE @SearchTextLike
        OR vc.DepartmentName LIKE @SearchTextLike
        OR vc.WorkTitle LIKE @SearchTextLike
        OR vc.AuthorDisplayName LIKE @SearchTextLike
        OR vc.WorkOpenAlexId LIKE @SearchTextLike
    )
    AND (@MinimumScore IS NULL OR vc.MatchScore >= @MinimumScore)
    AND
    (
        @InstitutionEvidenceFilter = 'Any'
        OR (@InstitutionEvidenceFilter = 'Present' AND ISNULL(vc.EvidenceSummary, '') LIKE '%AffiliatedInstitutionMatchCount=%' AND ISNULL(vc.EvidenceSummary, '') NOT LIKE '%AffiliatedInstitutionMatchCount=0%')
        OR (@InstitutionEvidenceFilter = 'Missing' AND (ISNULL(vc.EvidenceSummary, '') NOT LIKE '%AffiliatedInstitutionMatchCount=%' OR ISNULL(vc.EvidenceSummary, '') LIKE '%AffiliatedInstitutionMatchCount=0%'))
    )
    AND
    (
        @CoauthorEvidenceFilter = 'Any'
        OR (@CoauthorEvidenceFilter = 'Present' AND ISNULL(vc.EvidenceSummary, '') LIKE '%KnownCoauthorOverlapCount=%' AND ISNULL(vc.EvidenceSummary, '') NOT LIKE '%KnownCoauthorOverlapCount=0%')
        OR (@CoauthorEvidenceFilter = 'Missing' AND (ISNULL(vc.EvidenceSummary, '') NOT LIKE '%KnownCoauthorOverlapCount=%' OR ISNULL(vc.EvidenceSummary, '') LIKE '%KnownCoauthorOverlapCount=0%'))
    )
    AND
    (
        @NearDuplicateFilter = 'Any'
        OR (@NearDuplicateFilter = 'Warning' AND vc.NearDuplicateCandidateCount > 1 AND res.ResolutionStatus IS NULL)
        OR (@NearDuplicateFilter = 'Resolved' AND res.ResolutionStatus IS NOT NULL)
        OR (@NearDuplicateFilter = 'None' AND vc.NearDuplicateCandidateCount <= 1)
    )
    AND
    (
        @ResolutionStatusFilter = 'Any'
        OR (@ResolutionStatusFilter = 'Unresolved' AND vc.NearDuplicateCandidateCount > 1 AND res.ResolutionStatus IS NULL)
        OR (@ResolutionStatusFilter = 'Resolved' AND res.ResolutionStatus IS NOT NULL)
        OR res.ResolutionStatus = @ResolutionStatusFilter
    )
ORDER BY vc.CreatedAtUtc DESC;";

        command.Parameters.AddWithValue("@SearchTextLike", BuildSearchLikeValue(searchText));
        command.Parameters.AddWithValue("@MinimumScore", minimumScore.HasValue ? minimumScore.Value : DBNull.Value);
        command.Parameters.AddWithValue("@InstitutionEvidenceFilter", NormalizeFilter(institutionEvidenceFilter, new[] { "Any", "Present", "Missing" }));
        command.Parameters.AddWithValue("@CoauthorEvidenceFilter", NormalizeFilter(coauthorEvidenceFilter, new[] { "Any", "Present", "Missing" }));
        command.Parameters.AddWithValue("@NearDuplicateFilter", NormalizeFilter(nearDuplicateFilter, new[] { "Any", "Warning", "Resolved", "None" }));
        command.Parameters.AddWithValue("@ResolutionStatusFilter", NormalizeFilter(resolutionStatusFilter, new[] { "Any", "Resolved", "Unresolved", "DuplicateOfCandidate", "KeepSeparate", "SuppressWarning" }));

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

    public async Task<IReadOnlyList<CurrentPublicationReviewQueueItem>>
    GetCurrentPublicationQueueAsync(
        long departmentId,
        int top = 200,
        string? searchText = null,
        CancellationToken cancellationToken = default)
{
    var rows = new List<CurrentPublicationReviewQueueItem>();

    await using var connection = _connectionFactory.CreateConnection();
    await connection.OpenAsync(cancellationToken);

    if (!await ObjectExistsAsync(
            connection,
            "academiconline.vw_FacultyPublicationReviewStatus",
            "V",
            cancellationToken))
    {
        return rows;
    }

    await using var command = connection.CreateCommand();

    command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 500)})
    q.FacultyPublicationReviewCandidateId,
    q.UvaUserId,
    q.FacultyDisplayName,
    q.Email,
    q.DepartmentName,
    q.DivisionName,
    q.Title,
    q.Venue,
    q.PublicationYear,

    WorkDoi =
        COALESCE(
            NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
            NULLIF(LTRIM(RTRIM(q.Doi)), N'')
        ),

    DoiUrl =
        CASE
            WHEN COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                 ) IS NULL
                THEN NULL

            WHEN COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                 ) LIKE N'http://%'
              OR COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                 ) LIKE N'https://%'
                THEN COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                )

            ELSE
                N'https://doi.org/' +
                COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                )
                END,

            q.ReviewStatus,
            q.RelationshipSource,
            q.FirstSeenAtUtc,
            q.LastSeenAtUtc

        FROM academiconline.vw_FacultyPublicationReviewStatus q

        WHERE q.DepartmentId = @DepartmentId
        AND q.IsCurrentSource = 1
        AND q.ReviewStatus = N'Pending'

        AND
        (
            @SearchTextLike IS NULL
            OR q.UvaUserId LIKE @SearchTextLike
            OR q.FacultyDisplayName LIKE @SearchTextLike
            OR q.Email LIKE @SearchTextLike
            OR q.DepartmentName LIKE @SearchTextLike
            OR q.DivisionName LIKE @SearchTextLike
            OR q.Title LIKE @SearchTextLike
            OR q.Venue LIKE @SearchTextLike
            OR q.Doi LIKE @SearchTextLike
            OR q.DoiNormalized LIKE @SearchTextLike
        )

        ORDER BY
            q.FacultyDisplayName,
            q.PublicationYear DESC,
            q.Title,
            q.PublicationKey;";

            command.Parameters.AddWithValue(
                "@DepartmentId",
                departmentId);

            command.Parameters.AddWithValue(
                "@SearchTextLike",
                BuildSearchLikeValue(searchText));

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new CurrentPublicationReviewQueueItem
                {
                    FacultyPublicationReviewCandidateId =
                        reader.GetInt64(0),

                    UvaUserId =
                        reader.IsDBNull(1)
                            ? null
                            : reader.GetString(1),

                    FacultyDisplayName =
                        reader.GetString(2),

                    PrimaryEmail =
                        reader.IsDBNull(3)
                            ? null
                            : reader.GetString(3),

                    DepartmentName =
                        reader.GetString(4),

                    DivisionName =
                        reader.IsDBNull(5)
                            ? null
                            : reader.GetString(5),

                    WorkTitle =
                        reader.IsDBNull(6)
                            ? string.Empty
                            : reader.GetString(6),

                    Venue =
                        reader.IsDBNull(7)
                            ? null
                            : reader.GetString(7),

                    PublicationYear =
                        reader.IsDBNull(8)
                            ? null
                            : reader.GetInt32(8),

                    WorkDoi =
                        reader.IsDBNull(9)
                            ? null
                            : reader.GetString(9),

                    DoiUrl =
                        reader.IsDBNull(10)
                            ? null
                            : reader.GetString(10),

                    ReviewStatus =
                        reader.GetString(11),

                    RelationshipSource =
                        reader.GetString(12),

                    FirstSeenAtUtc =
                        reader.GetDateTime(13),

                    LastSeenAtUtc =
                        reader.GetDateTime(14)
                });
            }

            return rows;
        }

    public async Task<CurrentPublicationReviewStatusSummary>
    GetCurrentPublicationStatusSummaryAsync(
        long departmentId,
        CancellationToken cancellationToken = default)
{
    var result = new CurrentPublicationReviewStatusSummary();

    await using var connection = _connectionFactory.CreateConnection();
    await connection.OpenAsync(cancellationToken);

    if (!await ObjectExistsAsync(
            connection,
            "academiconline.vw_FacultyPublicationReviewStatus",
            "V",
            cancellationToken))
    {
        return result;
    }

    await using var command = connection.CreateCommand();

    command.CommandText = @"
SELECT
    TotalCurrentCount = COUNT_BIG(*),

    PendingCount =
        COALESCE(
            SUM(
                CASE
                    WHEN ReviewStatus = N'Pending'
                    THEN CONVERT(BIGINT, 1)
                    ELSE CONVERT(BIGINT, 0)
                END
            ),
            0
        ),

    AcceptedCount =
        COALESCE(
            SUM(
                CASE
                    WHEN ReviewStatus = N'Accepted'
                    THEN CONVERT(BIGINT, 1)
                    ELSE CONVERT(BIGINT, 0)
                END
            ),
            0
        ),

    RejectedCount =
        COALESCE(
            SUM(
                CASE
                    WHEN ReviewStatus = N'Rejected'
                    THEN CONVERT(BIGINT, 1)
                    ELSE CONVERT(BIGINT, 0)
                END
            ),
            0
        )

FROM academiconline.vw_FacultyPublicationReviewStatus

WHERE DepartmentId = @DepartmentId
  AND IsCurrentSource = 1;";

    command.Parameters.AddWithValue(
        "@DepartmentId",
        departmentId);

    await using var reader =
        await command.ExecuteReaderAsync(cancellationToken);

    if (await reader.ReadAsync(cancellationToken))
    {
        result.TotalCurrentCount = reader.GetInt64(0);
        result.PendingCount = reader.GetInt64(1);
        result.AcceptedCount = reader.GetInt64(2);
        result.RejectedCount = reader.GetInt64(3);
    }

    return result;
}


public async Task<IReadOnlyList<CurrentPublicationReviewStatusItem>>
    GetCurrentPublicationStatusItemsAsync(
        long departmentId,
        int top = 200,
        string statusFilter = "Any",
        string? searchText = null,
        CancellationToken cancellationToken = default)
{
    var rows = new List<CurrentPublicationReviewStatusItem>();

    await using var connection = _connectionFactory.CreateConnection();
    await connection.OpenAsync(cancellationToken);

    if (!await ObjectExistsAsync(
            connection,
            "academiconline.vw_FacultyPublicationReviewStatus",
            "V",
            cancellationToken))
    {
        return rows;
    }

    await using var command = connection.CreateCommand();

    command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 500)})
    q.FacultyPublicationReviewCandidateId,
    q.UvaUserId,
    q.FacultyDisplayName,
    q.Email,
    q.DepartmentName,
    q.DivisionName,
    q.Title,
    q.Venue,
    q.PublicationYear,

    WorkDoi =
        COALESCE(
            NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
            NULLIF(LTRIM(RTRIM(q.Doi)), N'')
        ),

    DoiUrl =
        CASE
            WHEN COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                 ) IS NULL
                THEN NULL

            WHEN COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                 ) LIKE N'http://%'
              OR COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                 ) LIKE N'https://%'
                THEN COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                )

            ELSE
                N'https://doi.org/' +
                COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                )
        END,

    q.ReviewStatus,
    q.DecisionStatus,
    q.DecisionReason,
    q.DecisionSource,
    q.ReviewerNotes,
    q.ReviewedBy,
    q.ReviewedAtUtc,
    q.RelationshipSource

FROM academiconline.vw_FacultyPublicationReviewStatus q

WHERE q.DepartmentId = @DepartmentId
  AND q.IsCurrentSource = 1

  AND
  (
      @StatusFilter = N'Any'
      OR q.ReviewStatus = @StatusFilter
  )

  AND
  (
      @SearchTextLike IS NULL
      OR q.UvaUserId LIKE @SearchTextLike
      OR q.FacultyDisplayName LIKE @SearchTextLike
      OR q.Email LIKE @SearchTextLike
      OR q.DepartmentName LIKE @SearchTextLike
      OR q.DivisionName LIKE @SearchTextLike
      OR q.Title LIKE @SearchTextLike
      OR q.Venue LIKE @SearchTextLike
      OR q.Doi LIKE @SearchTextLike
      OR q.DoiNormalized LIKE @SearchTextLike
  )

ORDER BY
    CASE q.ReviewStatus
        WHEN N'Accepted' THEN 1
        WHEN N'Rejected' THEN 2
        ELSE 3
    END,
    q.ReviewedAtUtc DESC,
    q.FacultyDisplayName,
    q.PublicationYear DESC,
    q.Title;";

    command.Parameters.AddWithValue(
        "@DepartmentId",
        departmentId);

    command.Parameters.AddWithValue(
        "@StatusFilter",
        NormalizeFilter(
            statusFilter,
            new[]
            {
                "Any",
                "Pending",
                "Accepted",
                "Rejected"
            }));

    command.Parameters.AddWithValue(
        "@SearchTextLike",
        BuildSearchLikeValue(searchText));

    await using var reader =
        await command.ExecuteReaderAsync(cancellationToken);

    while (await reader.ReadAsync(cancellationToken))
    {
        rows.Add(new CurrentPublicationReviewStatusItem
        {
            FacultyPublicationReviewCandidateId =
                reader.GetInt64(0),

            UvaUserId =
                reader.IsDBNull(1)
                    ? null
                    : reader.GetString(1),

            FacultyDisplayName =
                reader.GetString(2),

            PrimaryEmail =
                reader.IsDBNull(3)
                    ? null
                    : reader.GetString(3),

            DepartmentName =
                reader.GetString(4),

            DivisionName =
                reader.IsDBNull(5)
                    ? null
                    : reader.GetString(5),

            WorkTitle =
                reader.IsDBNull(6)
                    ? string.Empty
                    : reader.GetString(6),

            Venue =
                reader.IsDBNull(7)
                    ? null
                    : reader.GetString(7),

            PublicationYear =
                reader.IsDBNull(8)
                    ? null
                    : reader.GetInt32(8),

            WorkDoi =
                reader.IsDBNull(9)
                    ? null
                    : reader.GetString(9),

            DoiUrl =
                reader.IsDBNull(10)
                    ? null
                    : reader.GetString(10),

            ReviewStatus =
                reader.GetString(11),

            DecisionStatus =
                reader.IsDBNull(12)
                    ? null
                    : reader.GetString(12),

            DecisionReason =
                reader.IsDBNull(13)
                    ? null
                    : reader.GetString(13),

            DecisionSource =
                reader.IsDBNull(14)
                    ? null
                    : reader.GetString(14),

            ReviewerNotes =
                reader.IsDBNull(15)
                    ? null
                    : reader.GetString(15),

            ReviewedBy =
                reader.IsDBNull(16)
                    ? null
                    : reader.GetString(16),

            ReviewedAtUtc =
                reader.IsDBNull(17)
                    ? null
                    : reader.GetDateTime(17),

            RelationshipSource =
                reader.GetString(18)
        });
    }

    return rows;
}


public async Task<
    IReadOnlyList<CurrentPublicationReviewDecisionHistoryItem>>
    GetCurrentPublicationDecisionHistoryAsync(
        long departmentId,
        int top = 100,
        CancellationToken cancellationToken = default)
{
    var rows =
        new List<CurrentPublicationReviewDecisionHistoryItem>();

    await using var connection = _connectionFactory.CreateConnection();
    await connection.OpenAsync(cancellationToken);

    if (!await ObjectExistsAsync(
            connection,
            "academiconline.FacultyPublicationReviewDecision",
            "U",
            cancellationToken))
    {
        return rows;
    }

    await using var command = connection.CreateCommand();

    command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 500)})
    d.FacultyPublicationReviewDecisionId,
    c.FacultyPublicationReviewCandidateId,

    c.UvaUserId,
    c.FacultyDisplayName,

    c.PublicationKey,

    WorkDoi =
        COALESCE(
            NULLIF(LTRIM(RTRIM(c.DoiNormalized)), N''),
            NULLIF(LTRIM(RTRIM(c.Doi)), N'')
        ),

    c.Title,

    d.DecisionStatus,
    d.DecisionReason,
    d.DecisionSource,
    d.ReviewerNotes,
    d.ReviewedBy,
    d.ReviewedAtUtc,
    d.IsCurrent

FROM academiconline.FacultyPublicationReviewDecision d

JOIN academiconline.FacultyPublicationReviewCandidate c
    ON c.FacultyPublicationReviewCandidateId =
       d.FacultyPublicationReviewCandidateId

WHERE c.DepartmentId = @DepartmentId

ORDER BY
    d.ReviewedAtUtc DESC,
    d.FacultyPublicationReviewDecisionId DESC;";

    command.Parameters.AddWithValue(
        "@DepartmentId",
        departmentId);

    await using var reader =
        await command.ExecuteReaderAsync(cancellationToken);

    while (await reader.ReadAsync(cancellationToken))
    {
        rows.Add(new CurrentPublicationReviewDecisionHistoryItem
        {
            FacultyPublicationReviewDecisionId =
                reader.GetInt64(0),

            FacultyPublicationReviewCandidateId =
                reader.GetInt64(1),

            UvaUserId =
                reader.IsDBNull(2)
                    ? null
                    : reader.GetString(2),

            FacultyDisplayName =
                reader.GetString(3),

            PublicationKey =
                reader.GetInt64(4),

            WorkDoi =
                reader.IsDBNull(5)
                    ? null
                    : reader.GetString(5),

            WorkTitle =
                reader.IsDBNull(6)
                    ? string.Empty
                    : reader.GetString(6),

            DecisionStatus =
                reader.GetString(7),

            DecisionReason =
                reader.IsDBNull(8)
                    ? null
                    : reader.GetString(8),

            DecisionSource =
                reader.GetString(9),

            ReviewerNotes =
                reader.IsDBNull(10)
                    ? null
                    : reader.GetString(10),

            ReviewedBy =
                reader.GetString(11),

            ReviewedAtUtc =
                reader.GetDateTime(12),

            IsCurrent =
                reader.GetBoolean(13)
        });
    }

    return rows;
}

    public async Task<DepartmentReviewDashboardSummary>
    GetDepartmentReviewDashboardSummaryAsync(
        long departmentId,
        CancellationToken cancellationToken = default)
{
    var result = new DepartmentReviewDashboardSummary
    {
        DepartmentId = departmentId
    };

    await using var connection =
        _connectionFactory.CreateConnection();

    await connection.OpenAsync(cancellationToken);

    if (!await ObjectExistsAsync(
            connection,
            "academiconline.vw_FacultyPublicationReviewStatus",
            "V",
            cancellationToken))
    {
        return result;
    }

    await using var command =
        connection.CreateCommand();

    command.CommandText = @"
SELECT
    DepartmentId,

    DepartmentName =
        MAX(DepartmentName),

    TotalCount =
        COUNT_BIG(*),

    DistinctFacultyCount =
    CONVERT(
        BIGINT,
        COUNT(DISTINCT FacultyKey)
    ),

    PendingCount =
        COALESCE(
            SUM(
                CASE
                    WHEN ReviewStatus = N'Pending'
                    THEN CONVERT(BIGINT, 1)
                    ELSE CONVERT(BIGINT, 0)
                END
            ),
            0
        ),

    AcceptedCount =
        COALESCE(
            SUM(
                CASE
                    WHEN ReviewStatus = N'Accepted'
                    THEN CONVERT(BIGINT, 1)
                    ELSE CONVERT(BIGINT, 0)
                END
            ),
            0
        ),

    RejectedCount =
        COALESCE(
            SUM(
                CASE
                    WHEN ReviewStatus = N'Rejected'
                    THEN CONVERT(BIGINT, 1)
                    ELSE CONVERT(BIGINT, 0)
                END
            ),
            0
        )

FROM academiconline.vw_FacultyPublicationReviewStatus

WHERE DepartmentId = @DepartmentId
  AND IsCurrentSource = 1

GROUP BY DepartmentId;";

    command.Parameters.AddWithValue(
        "@DepartmentId",
        departmentId);

    await using var reader =
        await command.ExecuteReaderAsync(
            cancellationToken);

    if (await reader.ReadAsync(cancellationToken))
    {
        result.DepartmentId =
            reader.GetInt64(0);

        result.DepartmentName =
            reader.IsDBNull(1)
                ? string.Empty
                : reader.GetString(1);

        result.TotalCount =
            reader.GetInt64(2);

        result.DistinctFacultyCount =
            reader.GetInt64(3);

        result.PendingCount =
            reader.GetInt64(4);

        result.AcceptedCount =
            reader.GetInt64(5);

        result.RejectedCount =
            reader.GetInt64(6);
    }

    return result;
}


public async Task<
    IReadOnlyList<DepartmentFacultyReviewSummary>>
    GetDepartmentFacultyReviewSummaryAsync(
        long departmentId,
        CancellationToken cancellationToken = default)
{
    var rows =
        new List<DepartmentFacultyReviewSummary>();

    await using var connection =
        _connectionFactory.CreateConnection();

    await connection.OpenAsync(cancellationToken);

    if (!await ObjectExistsAsync(
            connection,
            "academiconline.vw_FacultyPublicationReviewStatus",
            "V",
            cancellationToken))
    {
        return rows;
    }

    await using var command =
        connection.CreateCommand();

    command.CommandText = @"
SELECT
    FacultyKey,

    UvaUserId =
        MAX(UvaUserId),

    FacultyDisplayName =
        MAX(FacultyDisplayName),

    Email =
        MAX(Email),

    TotalCount =
        COUNT_BIG(*),

    PendingCount =
        COALESCE(
            SUM(
                CASE
                    WHEN ReviewStatus = N'Pending'
                    THEN CONVERT(BIGINT, 1)
                    ELSE CONVERT(BIGINT, 0)
                END
            ),
            0
        ),

    AcceptedCount =
        COALESCE(
            SUM(
                CASE
                    WHEN ReviewStatus = N'Accepted'
                    THEN CONVERT(BIGINT, 1)
                    ELSE CONVERT(BIGINT, 0)
                END
            ),
            0
        ),

    RejectedCount =
        COALESCE(
            SUM(
                CASE
                    WHEN ReviewStatus = N'Rejected'
                    THEN CONVERT(BIGINT, 1)
                    ELSE CONVERT(BIGINT, 0)
                END
            ),
            0
        )

FROM academiconline.vw_FacultyPublicationReviewStatus

WHERE DepartmentId = @DepartmentId
  AND IsCurrentSource = 1

GROUP BY FacultyKey

ORDER BY
    MAX(FacultyDisplayName),
    MAX(UvaUserId);";

    command.Parameters.AddWithValue(
        "@DepartmentId",
        departmentId);

    await using var reader =
        await command.ExecuteReaderAsync(
            cancellationToken);

    while (await reader.ReadAsync(cancellationToken))
    {
        rows.Add(
            new DepartmentFacultyReviewSummary
            {
                FacultyKey =
                    reader.GetString(0),

                UvaUserId =
                    reader.IsDBNull(1)
                        ? null
                        : reader.GetString(1),

                FacultyDisplayName =
                    reader.GetString(2),

                Email =
                    reader.IsDBNull(3)
                        ? null
                        : reader.GetString(3),

                TotalCount =
                    reader.GetInt64(4),

                PendingCount =
                    reader.GetInt64(5),

                AcceptedCount =
                    reader.GetInt64(6),

                RejectedCount =
                    reader.GetInt64(7)
            });
    }

    return rows;
}

    public async Task<FacultyReviewWorkspaceSummary>
    GetFacultyReviewWorkspaceSummaryAsync(
        long departmentId,
        string facultyKey,
        CancellationToken cancellationToken = default)
{
    var result = new FacultyReviewWorkspaceSummary
    {
        DepartmentId = departmentId,
        FacultyKey = facultyKey
    };

    if (string.IsNullOrWhiteSpace(facultyKey))
    {
        return result;
    }

    await using var connection =
        _connectionFactory.CreateConnection();

    await connection.OpenAsync(cancellationToken);

    if (!await ObjectExistsAsync(
            connection,
            "academiconline.vw_FacultyPublicationReviewStatus",
            "V",
            cancellationToken))
    {
        return result;
    }

    await using var command =
        connection.CreateCommand();

    command.CommandText = @"
SELECT
    DepartmentId =
        MAX(DepartmentId),

    DepartmentName =
        MAX(DepartmentName),

    FacultyKey =
        MAX(FacultyKey),

    UvaUserId =
        MAX(UvaUserId),

    FacultyDisplayName =
        MAX(FacultyDisplayName),

    Email =
        MAX(Email),

    TotalCount =
        COUNT_BIG(*),

    PendingCount =
        COALESCE(
            SUM(
                CASE
                    WHEN ReviewStatus = N'Pending'
                    THEN CONVERT(BIGINT, 1)
                    ELSE CONVERT(BIGINT, 0)
                END
            ),
            0
        ),

    AcceptedCount =
        COALESCE(
            SUM(
                CASE
                    WHEN ReviewStatus = N'Accepted'
                    THEN CONVERT(BIGINT, 1)
                    ELSE CONVERT(BIGINT, 0)
                END
            ),
            0
        ),

    RejectedCount =
        COALESCE(
            SUM(
                CASE
                    WHEN ReviewStatus = N'Rejected'
                    THEN CONVERT(BIGINT, 1)
                    ELSE CONVERT(BIGINT, 0)
                END
            ),
            0
        )

FROM academiconline.vw_FacultyPublicationReviewStatus

WHERE DepartmentId = @DepartmentId
  AND FacultyKey = @FacultyKey
  AND IsCurrentSource = 1;";

    command.Parameters.AddWithValue(
        "@DepartmentId",
        departmentId);

    command.Parameters.AddWithValue(
        "@FacultyKey",
        facultyKey);

    await using var reader =
        await command.ExecuteReaderAsync(cancellationToken);

    if (await reader.ReadAsync(cancellationToken)
        && !reader.IsDBNull(2))
    {
        result.DepartmentId =
            reader.GetInt64(0);

        result.DepartmentName =
            reader.IsDBNull(1)
                ? string.Empty
                : reader.GetString(1);

        result.FacultyKey =
            reader.GetString(2);

        result.UvaUserId =
            reader.IsDBNull(3)
                ? null
                : reader.GetString(3);

        result.FacultyDisplayName =
            reader.IsDBNull(4)
                ? string.Empty
                : reader.GetString(4);

        result.Email =
            reader.IsDBNull(5)
                ? null
                : reader.GetString(5);

        result.TotalCount =
            reader.GetInt64(6);

        result.PendingCount =
            reader.GetInt64(7);

        result.AcceptedCount =
            reader.GetInt64(8);

        result.RejectedCount =
            reader.GetInt64(9);
    }

    return result;
}


public async Task<
    IReadOnlyList<FacultyReviewWorkspaceItem>>
    GetFacultyReviewWorkspaceItemsAsync(
        long departmentId,
        string facultyKey,
        string statusFilter = "Pending",
        int top = 200,
        CancellationToken cancellationToken = default)
{
    var rows =
        new List<FacultyReviewWorkspaceItem>();

    if (string.IsNullOrWhiteSpace(facultyKey))
    {
        return rows;
    }

    await using var connection =
        _connectionFactory.CreateConnection();

    await connection.OpenAsync(cancellationToken);

    if (!await ObjectExistsAsync(
            connection,
            "academiconline.vw_FacultyPublicationReviewStatus",
            "V",
            cancellationToken))
    {
        return rows;
    }

    await using var command =
        connection.CreateCommand();

    command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 500)})
    q.FacultyPublicationReviewCandidateId,
    q.PublicationKey,

    WorkDoi =
        COALESCE(
            NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
            NULLIF(LTRIM(RTRIM(q.Doi)), N'')
        ),

    DoiUrl =
        CASE
            WHEN COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                 ) IS NULL
                THEN NULL

            WHEN COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                 ) LIKE N'http://%'
              OR COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                 ) LIKE N'https://%'
                THEN COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                )

            ELSE
                N'https://doi.org/' +
                COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                )
        END,

    q.Title,
    q.Venue,
    q.PublicationYear,

    q.ReviewStatus,

    q.DecisionStatus,
    q.DecisionReason,

    q.ReviewerNotes,
    q.ReviewedBy,
    q.ReviewedAtUtc

FROM academiconline.vw_FacultyPublicationReviewStatus q

WHERE q.DepartmentId = @DepartmentId
  AND q.FacultyKey = @FacultyKey
  AND q.IsCurrentSource = 1

  AND
  (
      @StatusFilter = N'Any'
      OR q.ReviewStatus = @StatusFilter
  )

ORDER BY
    q.PublicationYear DESC,
    q.Title,
    q.PublicationKey;";

    command.Parameters.AddWithValue(
        "@DepartmentId",
        departmentId);

    command.Parameters.AddWithValue(
        "@FacultyKey",
        facultyKey);

    command.Parameters.AddWithValue(
        "@StatusFilter",
        NormalizeFilter(
            statusFilter,
            new[]
            {
                "Any",
                "Pending",
                "Accepted",
                "Rejected"
            }));

    await using var reader =
        await command.ExecuteReaderAsync(cancellationToken);

    while (await reader.ReadAsync(cancellationToken))
    {
        rows.Add(
            new FacultyReviewWorkspaceItem
            {
                FacultyPublicationReviewCandidateId =
                    reader.GetInt64(0),

                PublicationKey =
                    reader.GetInt64(1),

                WorkDoi =
                    reader.IsDBNull(2)
                        ? null
                        : reader.GetString(2),

                DoiUrl =
                    reader.IsDBNull(3)
                        ? null
                        : reader.GetString(3),

                WorkTitle =
                    reader.IsDBNull(4)
                        ? string.Empty
                        : reader.GetString(4),

                Venue =
                    reader.IsDBNull(5)
                        ? null
                        : reader.GetString(5),

                PublicationYear =
                    reader.IsDBNull(6)
                        ? null
                        : reader.GetInt32(6),

                ReviewStatus =
                    reader.GetString(7),

                DecisionStatus =
                    reader.IsDBNull(8)
                        ? null
                        : reader.GetString(8),

                DecisionReason =
                    reader.IsDBNull(9)
                        ? null
                        : reader.GetString(9),

                ReviewerNotes =
                    reader.IsDBNull(10)
                        ? null
                        : reader.GetString(10),

                ReviewedBy =
                    reader.IsDBNull(11)
                        ? null
                        : reader.GetString(11),

                ReviewedAtUtc =
                    reader.IsDBNull(12)
                        ? null
                        : reader.GetDateTime(12)
            });
    }

    return rows;
}

    public async Task<IReadOnlyList<int>>
    GetFacultyReviewWorkspaceYearsAsync(
        long departmentId,
        string facultyKey,
        CancellationToken cancellationToken = default)
{
    var years = new List<int>();

    if (string.IsNullOrWhiteSpace(facultyKey))
    {
        return years;
    }

    await using var connection =
        _connectionFactory.CreateConnection();

    await connection.OpenAsync(cancellationToken);

    if (!await ObjectExistsAsync(
            connection,
            "academiconline.vw_FacultyPublicationReviewStatus",
            "V",
            cancellationToken))
    {
        return years;
    }

    await using var command = connection.CreateCommand();

    command.CommandText = @"
SELECT DISTINCT
    PublicationYear
FROM academiconline.vw_FacultyPublicationReviewStatus
WHERE DepartmentId = @DepartmentId
  AND FacultyKey = @FacultyKey
  AND IsCurrentSource = 1
  AND PublicationYear IS NOT NULL
ORDER BY PublicationYear DESC;";

    command.Parameters.AddWithValue(
        "@DepartmentId",
        departmentId);

    command.Parameters.AddWithValue(
        "@FacultyKey",
        facultyKey);

    await using var reader =
        await command.ExecuteReaderAsync(cancellationToken);

    while (await reader.ReadAsync(cancellationToken))
    {
        years.Add(reader.GetInt32(0));
    }

    return years;
}


public async Task<FacultyReviewWorkspacePageResult>
    GetFacultyReviewWorkspacePageAsync(
        long departmentId,
        string facultyKey,
        string statusFilter = "Pending",
        int? yearFilter = null,
        string doiFilter = "Any",
        string reportingWindowFilter = "Any",
        string? searchText = null,
        int pageNumber = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
{
    var result =
        new FacultyReviewWorkspacePageResult();

    if (string.IsNullOrWhiteSpace(facultyKey))
    {
        return result;
    }

    pageNumber = Math.Max(1, pageNumber);
    pageSize = Math.Clamp(pageSize, 10, 100);

    var offset =
        (pageNumber - 1) * pageSize;

    await using var connection =
        _connectionFactory.CreateConnection();

    await connection.OpenAsync(cancellationToken);

    if (!await ObjectExistsAsync(
            connection,
            "academiconline.vw_FacultyPublicationReviewStatus",
            "V",
            cancellationToken))
    {
        return result;
    }

    await using var command =
        connection.CreateCommand();

    command.CommandText = @"
------------------------------------------------------------
-- Total filtered count
------------------------------------------------------------
SELECT
    COUNT_BIG(*)

FROM academiconline.vw_FacultyPublicationReviewStatus q

WHERE q.DepartmentId = @DepartmentId
  AND q.FacultyKey = @FacultyKey
  AND q.IsCurrentSource = 1

  AND
  (
      @StatusFilter = N'Any'
      OR q.ReviewStatus = @StatusFilter
  )

  AND
  (
      @YearFilter IS NULL
      OR q.PublicationYear = @YearFilter
  )

  AND
  (
      @DoiFilter = N'Any'

      OR
      (
          @DoiFilter = N'HasDoi'
          AND COALESCE(
                NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                NULLIF(LTRIM(RTRIM(q.Doi)), N'')
              ) IS NOT NULL
      )

      OR
      (
          @DoiFilter = N'NoDoi'
          AND COALESCE(
                NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                NULLIF(LTRIM(RTRIM(q.Doi)), N'')
              ) IS NULL
      )
  )

  AND
(
    @ReportingWindowFilter = N'Any'

    OR
    (
        @ReportingWindowFilter = N'Current'
        AND q.IsInCurrentReportingWindow = 1
    )

    OR
    (
        @ReportingWindowFilter = N'Historical'
        AND q.IsInCurrentReportingWindow = 0
    )
)

  AND
  (
      @SearchTextLike IS NULL
      OR q.Title LIKE @SearchTextLike
      OR q.Venue LIKE @SearchTextLike
      OR q.Doi LIKE @SearchTextLike
      OR q.DoiNormalized LIKE @SearchTextLike
  );


------------------------------------------------------------
-- Current page
------------------------------------------------------------
SELECT
    q.FacultyPublicationReviewCandidateId,
    q.PublicationKey,

    WorkDoi =
        COALESCE(
            NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
            NULLIF(LTRIM(RTRIM(q.Doi)), N'')
        ),

    DoiUrl =
        CASE
            WHEN COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                 ) IS NULL
                THEN NULL

            WHEN COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                 ) LIKE N'http://%'
              OR COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                 ) LIKE N'https://%'
                THEN COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                )

            ELSE
                N'https://doi.org/' +
                COALESCE(
                    NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                    NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                )
        END,

    q.Title,
    q.Venue,
    q.PublicationYear,

    q.ReviewStatus,
    q.IsInCurrentReportingWindow,
    q.ReportingWindowStatus,
    q.DecisionStatus,
    q.DecisionReason,
    q.ReviewerNotes,
    q.ReviewedBy,
    q.ReviewedAtUtc

FROM academiconline.vw_FacultyPublicationReviewStatus q

WHERE q.DepartmentId = @DepartmentId
  AND q.FacultyKey = @FacultyKey
  AND q.IsCurrentSource = 1

  AND
  (
      @StatusFilter = N'Any'
      OR q.ReviewStatus = @StatusFilter
  )

  AND
  (
      @YearFilter IS NULL
      OR q.PublicationYear = @YearFilter
  )

  AND
  (
      @DoiFilter = N'Any'

      OR
      (
          @DoiFilter = N'HasDoi'
          AND COALESCE(
                NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                NULLIF(LTRIM(RTRIM(q.Doi)), N'')
              ) IS NOT NULL
      )

      OR
      (
          @DoiFilter = N'NoDoi'
          AND COALESCE(
                NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                NULLIF(LTRIM(RTRIM(q.Doi)), N'')
              ) IS NULL
      )
  )

  AND
(
    @ReportingWindowFilter = N'Any'

    OR
    (
        @ReportingWindowFilter = N'Current'
        AND q.IsInCurrentReportingWindow = 1
    )

    OR
    (
        @ReportingWindowFilter = N'Historical'
        AND q.IsInCurrentReportingWindow = 0
    )
)

  AND
  (
      @SearchTextLike IS NULL
      OR q.Title LIKE @SearchTextLike
      OR q.Venue LIKE @SearchTextLike
      OR q.Doi LIKE @SearchTextLike
      OR q.DoiNormalized LIKE @SearchTextLike
  )

ORDER BY
    q.PublicationYear DESC,
    q.Title,
    q.PublicationKey

OFFSET @Offset ROWS
FETCH NEXT @PageSize ROWS ONLY;";

    command.Parameters.AddWithValue(
        "@DepartmentId",
        departmentId);

    command.Parameters.AddWithValue(
        "@FacultyKey",
        facultyKey);

    command.Parameters.AddWithValue(
        "@StatusFilter",
        NormalizeFilter(
            statusFilter,
            new[]
            {
                "Any",
                "Pending",
                "Accepted",
                "Rejected"
            }));

    command.Parameters.AddWithValue(
        "@YearFilter",
        yearFilter.HasValue
            ? yearFilter.Value
            : DBNull.Value);

    command.Parameters.AddWithValue(
        "@DoiFilter",
        NormalizeFilter(
            doiFilter,
            new[]
            {
                "Any",
                "HasDoi",
                "NoDoi"
            }));

    command.Parameters.AddWithValue(
    "@ReportingWindowFilter",
    NormalizeFilter(
        reportingWindowFilter,
        new[]
        {
            "Any",
            "Current",
            "Historical"
        }));

    

    command.Parameters.AddWithValue(
        "@SearchTextLike",
        BuildSearchLikeValue(searchText));

    command.Parameters.AddWithValue(
        "@Offset",
        offset);

    command.Parameters.AddWithValue(
        "@PageSize",
        pageSize);

    await using var reader =
        await command.ExecuteReaderAsync(cancellationToken);

    if (await reader.ReadAsync(cancellationToken))
    {
        result.TotalFilteredCount =
            reader.GetInt64(0);
    }

    await reader.NextResultAsync(cancellationToken);

    var rows =
        new List<FacultyReviewWorkspaceItem>();

    while (await reader.ReadAsync(cancellationToken))
    {
        rows.Add(
            new FacultyReviewWorkspaceItem
            {
                FacultyPublicationReviewCandidateId =
                    reader.GetInt64(0),

                PublicationKey =
                    reader.GetInt64(1),

                WorkDoi =
                    reader.IsDBNull(2)
                        ? null
                        : reader.GetString(2),

                DoiUrl =
                    reader.IsDBNull(3)
                        ? null
                        : reader.GetString(3),

                WorkTitle =
                    reader.IsDBNull(4)
                        ? string.Empty
                        : reader.GetString(4),

                Venue =
                    reader.IsDBNull(5)
                        ? null
                        : reader.GetString(5),

                PublicationYear =
                    reader.IsDBNull(6)
                        ? null
                        : reader.GetInt32(6),

                ReviewStatus =
                    reader.GetString(7),

                IsInCurrentReportingWindow =
                    reader.GetBoolean(8),

                ReportingWindowStatus =
                    reader.GetString(9),

                DecisionStatus =
                    reader.IsDBNull(10)
                        ? null
                        : reader.GetString(10),

                DecisionReason =
                    reader.IsDBNull(11)
                        ? null
                        : reader.GetString(11),

                ReviewerNotes =
                    reader.IsDBNull(12)
                        ? null
                        : reader.GetString(12),

                ReviewedBy =
                    reader.IsDBNull(13)
                        ? null
                        : reader.GetString(13),

                ReviewedAtUtc =
                    reader.IsDBNull(14)
                        ? null
                        : reader.GetDateTime(14)
                            });
                    }

                    result.Items = rows;

                    return result;
                }

    public async Task<IReadOnlyList<ReviewDepartmentOption>>
    GetReviewDepartmentOptionsAsync(
        CancellationToken cancellationToken = default)
{
    var rows = new List<ReviewDepartmentOption>();

    await using var connection =
        _connectionFactory.CreateConnection();

    await connection.OpenAsync(cancellationToken);

    if (!await ObjectExistsAsync(
            connection,
            "academiconline.vw_FacultyPublicationSearch",
            "V",
            cancellationToken))
    {
        return rows;
    }

    await using var command =
        connection.CreateCommand();

    command.CommandText = @"
SELECT
    DepartmentId,
    DepartmentName = MAX(DepartmentName)

FROM academiconline.vw_FacultyPublicationSearch

WHERE DepartmentDataComplete = 1

GROUP BY DepartmentId

ORDER BY
    MAX(DepartmentName),
    DepartmentId;";

    await using var reader =
        await command.ExecuteReaderAsync(
            cancellationToken);

    while (await reader.ReadAsync(cancellationToken))
    {
        rows.Add(
            new ReviewDepartmentOption
            {
                DepartmentId =
                    reader.GetInt64(0),

                DepartmentName =
                    reader.GetString(1)
            });
    }

    return rows;
}

    private static object BuildSearchLikeValue(string? searchText)
    {
        return string.IsNullOrWhiteSpace(searchText)
            ? DBNull.Value
            : $"%{searchText.Trim()}%";
    }

    private static string NormalizeFilter(string? value, IReadOnlyCollection<string> allowedValues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Any";
        }

        return allowedValues.FirstOrDefault(allowed => string.Equals(allowed, value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Any";
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
