using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class PublicationEvidenceEnhancementRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public PublicationEvidenceEnhancementRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PublicationEvidenceEnhancementResult> EnhancePublicationCandidatesAsync(
        long ingestionRunId,
        string? pilotCohortName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyPublicationCandidate", "U", cancellationToken))
        {
            return new PublicationEvidenceEnhancementResult
            {
                Skipped = true,
                SkipReason = "openalex.FacultyPublicationCandidate does not exist."
            };
        }

        if (!await ObjectExistsAsync(connection, "openalex.AffiliatedInstitutionAlias", "U", cancellationToken))
        {
            return new PublicationEvidenceEnhancementResult
            {
                Skipped = true,
                SkipReason = "openalex.AffiliatedInstitutionAlias does not exist. Run sql/260_V3E_PATCH007_AffiliatedInstitutionAlias_SQL2016.sql first."
            };
        }

        if (!await ObjectExistsAsync(connection, "openalex.WorkAuthorship", "U", cancellationToken))
        {
            return new PublicationEvidenceEnhancementResult
            {
                Skipped = true,
                SkipReason = "openalex.WorkAuthorship does not exist."
            };
        }

        if (!await ObjectExistsAsync(connection, "openalex.WorkAuthorshipInstitution", "U", cancellationToken))
        {
            return new PublicationEvidenceEnhancementResult
            {
                Skipped = true,
                SkipReason = "openalex.WorkAuthorshipInstitution does not exist."
            };
        }

        if (!await ObjectExistsAsync(connection, "openalex.Institution", "U", cancellationToken))
        {
            return new PublicationEvidenceEnhancementResult
            {
                Skipped = true,
                SkipReason = "openalex.Institution does not exist."
            };
        }

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = @"
SET NOCOUNT ON;

DECLARE @Evidence TABLE
(
    FacultyPublicationCandidateId BIGINT NOT NULL PRIMARY KEY,
    AffiliatedInstitutionMatchCount INT NOT NULL,
    AffiliatedInstitutionEvidenceWeight INT NOT NULL,
    AffiliatedInstitutionNames NVARCHAR(MAX) NULL,
    KnownCoauthorOverlapCount INT NOT NULL,
    KnownCoauthorHistoricalPublicationCount INT NOT NULL,
    KnownCoauthorNames NVARCHAR(MAX) NULL,
    EvidenceBoost DECIMAL(9,4) NOT NULL
);

INSERT INTO @Evidence
(
    FacultyPublicationCandidateId,
    AffiliatedInstitutionMatchCount,
    AffiliatedInstitutionEvidenceWeight,
    AffiliatedInstitutionNames,
    KnownCoauthorOverlapCount,
    KnownCoauthorHistoricalPublicationCount,
    KnownCoauthorNames,
    EvidenceBoost
)
SELECT
    pc.FacultyPublicationCandidateId,
    ISNULL(aff.AffiliatedInstitutionMatchCount, 0) AS AffiliatedInstitutionMatchCount,
    ISNULL(aff.AffiliatedInstitutionEvidenceWeight, 0) AS AffiliatedInstitutionEvidenceWeight,
    affNames.AffiliatedInstitutionNames,
    ISNULL(co.KnownCoauthorOverlapCount, 0) AS KnownCoauthorOverlapCount,
    ISNULL(co.KnownCoauthorHistoricalPublicationCount, 0) AS KnownCoauthorHistoricalPublicationCount,
    coNames.KnownCoauthorNames,
    CAST(
        (
            CASE
                WHEN ISNULL(aff.AffiliatedInstitutionEvidenceWeight, 0) >= 90 THEN 15
                WHEN ISNULL(aff.AffiliatedInstitutionEvidenceWeight, 0) >= 70 THEN 10
                WHEN ISNULL(aff.AffiliatedInstitutionMatchCount, 0) > 0 THEN 5
                ELSE 0
            END
            +
            CASE
                WHEN ISNULL(co.KnownCoauthorOverlapCount, 0) >= 3 THEN 20
                WHEN ISNULL(co.KnownCoauthorOverlapCount, 0) = 2 THEN 15
                WHEN ISNULL(co.KnownCoauthorOverlapCount, 0) = 1 THEN 10
                ELSE 0
            END
            +
            CASE
                WHEN ISNULL(co.KnownCoauthorHistoricalPublicationCount, 0) >= 10 THEN 10
                WHEN ISNULL(co.KnownCoauthorHistoricalPublicationCount, 0) >= 5 THEN 5
                WHEN ISNULL(co.KnownCoauthorHistoricalPublicationCount, 0) >= 2 THEN 2
                ELSE 0
            END
        ) AS DECIMAL(9,4)
    ) AS EvidenceBoost
FROM openalex.FacultyPublicationCandidate pc
OUTER APPLY
(
    SELECT
        COUNT(DISTINCT i.OpenAlexId) AS AffiliatedInstitutionMatchCount,
        MAX(aia.EvidenceWeight) AS AffiliatedInstitutionEvidenceWeight
    FROM openalex.WorkAuthorship waAll
    JOIN openalex.WorkAuthorshipInstitution wai
        ON waAll.WorkAuthorshipId = wai.WorkAuthorshipId
    JOIN openalex.Institution i
        ON wai.InstitutionOpenAlexId = i.OpenAlexId
    JOIN openalex.AffiliatedInstitutionAlias aia
        ON aia.IsActive = 1
       AND
       (
           LOWER(LTRIM(RTRIM(i.DisplayName))) = aia.NormalizedInstitutionName
           OR LOWER(LTRIM(RTRIM(i.DisplayName))) LIKE '%' + aia.NormalizedInstitutionName + '%'
       )
    WHERE waAll.WorkId = pc.WorkId
) aff
OUTER APPLY
(
    SELECT
        STUFF
        (
            (
                SELECT DISTINCT ', ' + i2.DisplayName
                FROM openalex.WorkAuthorship waAll2
                JOIN openalex.WorkAuthorshipInstitution wai2
                    ON waAll2.WorkAuthorshipId = wai2.WorkAuthorshipId
                JOIN openalex.Institution i2
                    ON wai2.InstitutionOpenAlexId = i2.OpenAlexId
                JOIN openalex.AffiliatedInstitutionAlias aia2
                    ON aia2.IsActive = 1
                   AND
                   (
                       LOWER(LTRIM(RTRIM(i2.DisplayName))) = aia2.NormalizedInstitutionName
                       OR LOWER(LTRIM(RTRIM(i2.DisplayName))) LIKE '%' + aia2.NormalizedInstitutionName + '%'
                   )
                WHERE waAll2.WorkId = pc.WorkId
                FOR XML PATH(''), TYPE
            ).value('.', 'NVARCHAR(MAX)'),
            1,
            2,
            ''
        ) AS AffiliatedInstitutionNames
) affNames
OUTER APPLY
(
    SELECT
        COUNT(DISTINCT newWa.AuthorOpenAlexId) AS KnownCoauthorOverlapCount,
        COUNT(DISTINCT priorDecision.WorkId) AS KnownCoauthorHistoricalPublicationCount
    FROM openalex.WorkAuthorship newWa
    JOIN openalex.WorkAuthorship priorWa
        ON priorWa.AuthorOpenAlexId = newWa.AuthorOpenAlexId
    JOIN openalex.FacultyPublicationDecision priorDecision
        ON priorDecision.WorkId = priorWa.WorkId
       AND priorDecision.FacultyId = pc.FacultyId
       AND priorDecision.IsCurrent = 1
       AND priorDecision.DecisionStatus = 'Accepted'
       AND priorDecision.WorkId <> pc.WorkId
    WHERE newWa.WorkId = pc.WorkId
      AND newWa.AuthorOpenAlexId IS NOT NULL
      AND newWa.AuthorOpenAlexId <> pc.AuthorOpenAlexId
) co
OUTER APPLY
(
    SELECT
        STUFF
        (
            (
                SELECT DISTINCT ', ' + ISNULL(newWa2.AuthorDisplayName, newWa2.AuthorOpenAlexId)
                FROM openalex.WorkAuthorship newWa2
                JOIN openalex.WorkAuthorship priorWa2
                    ON priorWa2.AuthorOpenAlexId = newWa2.AuthorOpenAlexId
                JOIN openalex.FacultyPublicationDecision priorDecision2
                    ON priorDecision2.WorkId = priorWa2.WorkId
                   AND priorDecision2.FacultyId = pc.FacultyId
                   AND priorDecision2.IsCurrent = 1
                   AND priorDecision2.DecisionStatus = 'Accepted'
                   AND priorDecision2.WorkId <> pc.WorkId
                WHERE newWa2.WorkId = pc.WorkId
                  AND newWa2.AuthorOpenAlexId IS NOT NULL
                  AND newWa2.AuthorOpenAlexId <> pc.AuthorOpenAlexId
                FOR XML PATH(''), TYPE
            ).value('.', 'NVARCHAR(MAX)'),
            1,
            2,
            ''
        ) AS KnownCoauthorNames
) coNames
WHERE pc.IngestionRunId = @IngestionRunId
  AND (@PilotCohortName IS NULL OR EXISTS
      (
          SELECT 1
          FROM openalex.PilotFacultyCohort cohort
          WHERE cohort.FacultyId = pc.FacultyId
            AND cohort.CohortName = @PilotCohortName
            AND cohort.IsEnabled = 1
      ));

DECLARE @CandidateRowsEvaluated INT = (SELECT COUNT(*) FROM @Evidence);
DECLARE @CandidateRowsEnhanced INT = (SELECT COUNT(*) FROM @Evidence WHERE EvidenceBoost > 0);
DECLARE @RowsWithAffiliatedInstitutionEvidence INT = (SELECT COUNT(*) FROM @Evidence WHERE AffiliatedInstitutionMatchCount > 0);
DECLARE @RowsWithKnownCoauthorEvidence INT = (SELECT COUNT(*) FROM @Evidence WHERE KnownCoauthorOverlapCount > 0);

UPDATE pc
SET
    MatchScore = CASE
        WHEN pc.MatchScore + e.EvidenceBoost > 100 THEN 100
        ELSE pc.MatchScore + e.EvidenceBoost
    END,
    EvidenceSummary = ISNULL(pc.EvidenceSummary, '')
        + '; PATCH007Evidence: AffiliatedInstitutionMatchCount=' + CONVERT(NVARCHAR(20), e.AffiliatedInstitutionMatchCount)
        + '; AffiliatedInstitutionEvidenceWeight=' + CONVERT(NVARCHAR(20), e.AffiliatedInstitutionEvidenceWeight)
        + '; AffiliatedInstitutionNames=' + ISNULL(e.AffiliatedInstitutionNames, '(none)')
        + '; KnownCoauthorOverlapCount=' + CONVERT(NVARCHAR(20), e.KnownCoauthorOverlapCount)
        + '; KnownCoauthorHistoricalPublicationCount=' + CONVERT(NVARCHAR(20), e.KnownCoauthorHistoricalPublicationCount)
        + '; KnownCoauthorNames=' + ISNULL(e.KnownCoauthorNames, '(none)')
        + '; EvidenceBoost=' + CONVERT(NVARCHAR(20), e.EvidenceBoost),
    EvidenceJson = '{'
        + '""Patch007AffiliatedInstitutionMatchCount"":' + CONVERT(NVARCHAR(20), e.AffiliatedInstitutionMatchCount) + ','
        + '""Patch007AffiliatedInstitutionEvidenceWeight"":' + CONVERT(NVARCHAR(20), e.AffiliatedInstitutionEvidenceWeight) + ','
        + '""Patch007KnownCoauthorOverlapCount"":' + CONVERT(NVARCHAR(20), e.KnownCoauthorOverlapCount) + ','
        + '""Patch007KnownCoauthorHistoricalPublicationCount"":' + CONVERT(NVARCHAR(20), e.KnownCoauthorHistoricalPublicationCount) + ','
        + '""Patch007EvidenceBoost"":' + CONVERT(NVARCHAR(20), e.EvidenceBoost)
        + '}',
    UpdatedAtUtc = SYSUTCDATETIME()
FROM openalex.FacultyPublicationCandidate pc
JOIN @Evidence e
    ON pc.FacultyPublicationCandidateId = e.FacultyPublicationCandidateId;

SELECT
    @CandidateRowsEvaluated AS CandidateRowsEvaluated,
    @CandidateRowsEnhanced AS CandidateRowsEnhanced,
    @RowsWithAffiliatedInstitutionEvidence AS RowsWithAffiliatedInstitutionEvidence,
    @RowsWithKnownCoauthorEvidence AS RowsWithKnownCoauthorEvidence;
";

        command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);
        command.Parameters.AddWithValue("@PilotCohortName", string.IsNullOrWhiteSpace(pilotCohortName) ? DBNull.Value : pilotCohortName.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new PublicationEvidenceEnhancementResult
            {
                Skipped = true,
                SkipReason = "PATCH007 evidence enhancement did not return a result row."
            };
        }

        return new PublicationEvidenceEnhancementResult
        {
            CandidateRowsEvaluated = reader.GetInt32(reader.GetOrdinal("CandidateRowsEvaluated")),
            CandidateRowsEnhanced = reader.GetInt32(reader.GetOrdinal("CandidateRowsEnhanced")),
            RowsWithAffiliatedInstitutionEvidence = reader.GetInt32(reader.GetOrdinal("RowsWithAffiliatedInstitutionEvidence")),
            RowsWithKnownCoauthorEvidence = reader.GetInt32(reader.GetOrdinal("RowsWithKnownCoauthorEvidence"))
        };
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
