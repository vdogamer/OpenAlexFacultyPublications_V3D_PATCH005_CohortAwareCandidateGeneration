using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

/// <summary>
/// Loads the authors appearing on the explicitly selected PATCH017B1 works.
/// The query uses a two-stage CTE so SQL Server 2016 never aggregates an
/// expression that directly contains a subquery.
/// </summary>
public sealed class FacultyAuthorIdentityResolutionSourceRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyAuthorIdentityResolutionSourceRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
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
CandidateAuthorshipRows AS
(
    SELECT
        wa.WorkAuthorshipId,
        wa.WorkId,
        wa.AuthorOpenAlexId,
        wa.AuthorDisplayName,
        wa.AuthorOrcid,
        wa.IsCorresponding,
        AuthorshipInstitutionCount =
        (
            SELECT COUNT(*)
            FROM openalex.WorkAuthorshipInstitution wai
            WHERE wai.WorkAuthorshipId = wa.WorkAuthorshipId
        ),
        HasUvaAffiliation = CASE WHEN EXISTS
        (
            SELECT 1
            FROM openalex.WorkAuthorshipInstitution wai
            WHERE wai.WorkAuthorshipId = wa.WorkAuthorshipId
              AND
              (
                  wai.InstitutionDisplayName LIKE '%University of Virginia%'
                  OR UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(wai.InstitutionOpenAlexId, ''))), 'https://openalex.org/', ''), 'http://openalex.org/', '')) IN
                  (
                      'I4210107925',
                      'I4210119250',
                      'I4210134211'
                  )
              )
        ) THEN 1 ELSE 0 END
    FROM openalex.WorkAuthorship wa
    JOIN SelectedWorks sw ON sw.WorkId = wa.WorkId
    WHERE NULLIF(LTRIM(RTRIM(wa.AuthorOpenAlexId)), '') IS NOT NULL
),
CandidateAuthors AS
(
    SELECT
        AuthorOpenAlexId,
        CandidateAuthorDisplayName = MAX(AuthorDisplayName),
        CandidateAuthorOrcid = MAX(AuthorOrcid),
        WorkAppearanceCount = COUNT(DISTINCT WorkId),
        CorrespondingAuthorWorkCount = SUM(CASE WHEN IsCorresponding = 1 THEN 1 ELSE 0 END),
        AuthorshipInstitutionCount = SUM(AuthorshipInstitutionCount),
        UvaAffiliationWorkCount = SUM(HasUvaAffiliation)
    FROM CandidateAuthorshipRows
    GROUP BY AuthorOpenAlexId
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
}
