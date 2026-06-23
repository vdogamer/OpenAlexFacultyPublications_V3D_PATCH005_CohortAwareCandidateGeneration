using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class AcceptedIdentityPublicationBridgeRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public AcceptedIdentityPublicationBridgeRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AcceptedIdentityPublicationBridgeResult> SyncAcceptedIdentitiesAsync(
        long ingestionRunId,
        string? pilotCohortName,
        string reviewedBy,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidateDecision", "U", cancellationToken))
        {
            return new AcceptedIdentityPublicationBridgeResult
            {
                Skipped = true,
                SkipReason = "openalex.FacultyIdentityCandidateDecision does not exist. Run PATCH005 decision table script first."
            };
        }

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityCandidateSummary", "U", cancellationToken))
        {
            return new AcceptedIdentityPublicationBridgeResult
            {
                Skipped = true,
                SkipReason = "openalex.FacultyIdentityCandidateSummary does not exist. Run PATCH004 summary generation first."
            };
        }

        if (!await ObjectExistsAsync(connection, "openalex.FacultyAuthorMatchCandidate", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.FacultyAuthorMatchCandidate does not exist. Run the author review schema first.");
        }

        if (!await ObjectExistsAsync(connection, "openalex.FacultyAuthorMatchDecision", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.FacultyAuthorMatchDecision does not exist. Run the author review schema first.");
        }

        if (!string.IsNullOrWhiteSpace(pilotCohortName) && !await ObjectExistsAsync(connection, "openalex.PilotFacultyCohort", "U", cancellationToken))
        {
            throw new InvalidOperationException("PilotCohortName was provided, but openalex.PilotFacultyCohort does not exist.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SET NOCOUNT ON;

DECLARE @Source TABLE
(
    FacultyIdentityCandidateDecisionId BIGINT NOT NULL,
    FacultyId BIGINT NOT NULL,
    FacultyDisplayName NVARCHAR(512) NULL,
    CandidateAuthorOpenAlexId VARCHAR(128) NOT NULL,
    CandidateAuthorOpenAlexShortId VARCHAR(64) NULL,
    CandidateDisplayName NVARCHAR(512) NULL,
    CandidateOrcid NVARCHAR(256) NULL,
    BestIdentityEvidenceScore DECIMAL(9,4) NOT NULL,
    SummaryText NVARCHAR(MAX) NULL,
    EvidenceJson NVARCHAR(MAX) NULL,
    AuthorId BIGINT NULL
);

INSERT INTO @Source
(
    FacultyIdentityCandidateDecisionId,
    FacultyId,
    FacultyDisplayName,
    CandidateAuthorOpenAlexId,
    CandidateAuthorOpenAlexShortId,
    CandidateDisplayName,
    CandidateOrcid,
    BestIdentityEvidenceScore,
    SummaryText,
    EvidenceJson,
    AuthorId
)
SELECT
    d.FacultyIdentityCandidateDecisionId,
    s.FacultyId,
    s.FacultyDisplayName,
    s.CandidateAuthorOpenAlexId,
    s.CandidateAuthorOpenAlexShortId,
    s.CandidateDisplayName,
    s.CandidateOrcid,
    s.BestIdentityEvidenceScore,
    s.SummaryText,
    s.EvidenceJson,
    a.AuthorId
FROM openalex.FacultyIdentityCandidateDecision d
JOIN openalex.FacultyIdentityCandidateSummary s
    ON d.FacultyId = s.FacultyId
   AND d.CandidateAuthorOpenAlexId = s.CandidateAuthorOpenAlexId
OUTER APPLY
(
    SELECT TOP (1)
        a.AuthorId
    FROM openalex.Author a
    WHERE a.OpenAlexId = s.CandidateAuthorOpenAlexId
       OR a.OpenAlexShortId = s.CandidateAuthorOpenAlexShortId
       OR
       (
           s.CandidateOrcid IS NOT NULL
           AND a.Orcid IS NOT NULL
           AND a.Orcid = s.CandidateOrcid
       )
    ORDER BY a.AuthorId
) a
WHERE d.IsCurrent = 1
  AND d.DecisionStatus = 'AcceptedIdentity'
  AND
  (
      @PilotCohortName IS NULL
      OR EXISTS
      (
          SELECT 1
          FROM openalex.PilotFacultyCohort pc
          WHERE pc.FacultyId = s.FacultyId
            AND pc.CohortName = @PilotCohortName
            AND pc.IsEnabled = 1
      )
  );

DECLARE @AcceptedIdentityRows INT = (SELECT COUNT(*) FROM @Source);
DECLARE @CandidateRowsUpdated INT = 0;
DECLARE @CandidateRowsInserted INT = 0;
DECLARE @DecisionRowsInserted INT = 0;
DECLARE @ExistingAcceptedAuthorDecisionsSkipped INT = 0;

UPDATE c
SET
    c.AuthorId = s.AuthorId,
    c.AuthorOpenAlexShortId = s.CandidateAuthorOpenAlexShortId,
    c.AuthorDisplayName = s.CandidateDisplayName,
    c.AuthorOrcid = s.CandidateOrcid,
    c.CandidateStatus = 'AutoAcceptedCandidate',
    c.MatchScore = s.BestIdentityEvidenceScore,
    c.NameScore = s.BestIdentityEvidenceScore,
    c.OrcidScore = CASE WHEN s.CandidateOrcid IS NULL THEN 0 ELSE 100 END,
    c.InstitutionScore = 0,
    c.CoauthorScore = 0,
    c.PublicationScore = 0,
    c.EvidenceSummary = 'AcceptedIdentityDecision: identity review accepted OpenAlex author ' + ISNULL(s.CandidateAuthorOpenAlexShortId, s.CandidateAuthorOpenAlexId) + ' for faculty ' + ISNULL(s.FacultyDisplayName, CONVERT(NVARCHAR(50), s.FacultyId)) + '. IdentityScore=' + CONVERT(NVARCHAR(50), s.BestIdentityEvidenceScore) + '.',
    c.EvidenceJson = s.EvidenceJson,
    c.IngestionRunId = @IngestionRunId,
    c.UpdatedAtUtc = SYSUTCDATETIME()
FROM openalex.FacultyAuthorMatchCandidate c
JOIN @Source s
    ON c.FacultyId = s.FacultyId
   AND c.AuthorOpenAlexId = s.CandidateAuthorOpenAlexId
   AND c.CandidateSource = 'AcceptedIdentityDecision';

SET @CandidateRowsUpdated = @@ROWCOUNT;

DECLARE @InsertedCandidates TABLE
(
    FacultyAuthorMatchCandidateId BIGINT NOT NULL
);

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
OUTPUT INSERTED.FacultyAuthorMatchCandidateId
INTO @InsertedCandidates
SELECT
    s.FacultyId,
    s.AuthorId,
    s.CandidateAuthorOpenAlexId,
    s.CandidateAuthorOpenAlexShortId,
    s.CandidateDisplayName,
    s.CandidateOrcid,
    'AcceptedIdentityDecision',
    'AutoAcceptedCandidate',
    s.BestIdentityEvidenceScore,
    s.BestIdentityEvidenceScore,
    CASE WHEN s.CandidateOrcid IS NULL THEN 0 ELSE 100 END,
    0,
    0,
    0,
    'AcceptedIdentityDecision: identity review accepted OpenAlex author ' + ISNULL(s.CandidateAuthorOpenAlexShortId, s.CandidateAuthorOpenAlexId) + ' for faculty ' + ISNULL(s.FacultyDisplayName, CONVERT(NVARCHAR(50), s.FacultyId)) + '. IdentityScore=' + CONVERT(NVARCHAR(50), s.BestIdentityEvidenceScore) + '.',
    s.EvidenceJson,
    @IngestionRunId,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM @Source s
WHERE NOT EXISTS
(
    SELECT 1
    FROM openalex.FacultyAuthorMatchCandidate c
    WHERE c.FacultyId = s.FacultyId
      AND c.AuthorOpenAlexId = s.CandidateAuthorOpenAlexId
      AND c.CandidateSource = 'AcceptedIdentityDecision'
);

SET @CandidateRowsInserted = @@ROWCOUNT;

DECLARE @DecisionSources TABLE
(
    FacultyAuthorMatchCandidateId BIGINT NOT NULL,
    FacultyId BIGINT NOT NULL,
    AuthorId BIGINT NULL,
    AuthorOpenAlexId VARCHAR(128) NOT NULL,
    AuthorDisplayName NVARCHAR(512) NULL,
    AuthorOrcid NVARCHAR(256) NULL
);

INSERT INTO @DecisionSources
(
    FacultyAuthorMatchCandidateId,
    FacultyId,
    AuthorId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid
)
SELECT
    c.FacultyAuthorMatchCandidateId,
    c.FacultyId,
    c.AuthorId,
    c.AuthorOpenAlexId,
    c.AuthorDisplayName,
    c.AuthorOrcid
FROM openalex.FacultyAuthorMatchCandidate c
JOIN @Source s
    ON c.FacultyId = s.FacultyId
   AND c.AuthorOpenAlexId = s.CandidateAuthorOpenAlexId
   AND c.CandidateSource = 'AcceptedIdentityDecision'
WHERE NOT EXISTS
(
    SELECT 1
    FROM openalex.FacultyAuthorMatchDecision d
    WHERE d.FacultyId = c.FacultyId
      AND d.AuthorOpenAlexId = c.AuthorOpenAlexId
      AND d.IsCurrent = 1
      AND d.DecisionStatus = 'Accepted'
);

SET @ExistingAcceptedAuthorDecisionsSkipped = @AcceptedIdentityRows - (SELECT COUNT(*) FROM @DecisionSources);

UPDATE d
SET
    d.IsCurrent = 0,
    d.UpdatedAtUtc = SYSUTCDATETIME()
FROM openalex.FacultyAuthorMatchDecision d
JOIN @DecisionSources s
    ON d.FacultyId = s.FacultyId
   AND d.AuthorOpenAlexId = s.AuthorOpenAlexId
WHERE d.IsCurrent = 1;

INSERT INTO openalex.FacultyAuthorMatchDecision
(
    FacultyAuthorMatchCandidateId,
    FacultyId,
    AuthorId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    DecisionStatus,
    DecisionReason,
    DecisionSource,
    IsCurrent,
    ReviewedBy,
    ReviewedAtUtc,
    ReviewerNotes,
    CreatedAtUtc,
    UpdatedAtUtc
)
SELECT
    s.FacultyAuthorMatchCandidateId,
    s.FacultyId,
    s.AuthorId,
    s.AuthorOpenAlexId,
    s.AuthorDisplayName,
    s.AuthorOrcid,
    'Accepted',
    'AcceptedIdentityDecision',
    'IdentityReviewBridge',
    1,
    @ReviewedBy,
    SYSUTCDATETIME(),
    'Generated from current AcceptedIdentity decision in openalex.FacultyIdentityCandidateDecision.',
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM @DecisionSources s;

SET @DecisionRowsInserted = @@ROWCOUNT;

SELECT
    @AcceptedIdentityRows AS AcceptedIdentityRows,
    @CandidateRowsInserted AS AuthorCandidateRowsInserted,
    @CandidateRowsUpdated AS AuthorCandidateRowsUpdated,
    @DecisionRowsInserted AS AuthorDecisionRowsInserted,
    @ExistingAcceptedAuthorDecisionsSkipped AS ExistingAcceptedAuthorDecisionsSkipped;
";

        command.Parameters.AddWithValue("@IngestionRunId", ingestionRunId);
        command.Parameters.AddWithValue("@PilotCohortName", string.IsNullOrWhiteSpace(pilotCohortName) ? DBNull.Value : pilotCohortName.Trim());
        command.Parameters.AddWithValue("@ReviewedBy", string.IsNullOrWhiteSpace(reviewedBy) ? "OpenAlex.PATCH006.IdentityReviewBridge" : reviewedBy);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new AcceptedIdentityPublicationBridgeResult
            {
                Skipped = true,
                SkipReason = "Accepted identity bridge did not return a result row."
            };
        }

        return new AcceptedIdentityPublicationBridgeResult
        {
            AcceptedIdentityRows = reader.GetInt32(reader.GetOrdinal("AcceptedIdentityRows")),
            AuthorCandidateRowsInserted = reader.GetInt32(reader.GetOrdinal("AuthorCandidateRowsInserted")),
            AuthorCandidateRowsUpdated = reader.GetInt32(reader.GetOrdinal("AuthorCandidateRowsUpdated")),
            AuthorDecisionRowsInserted = reader.GetInt32(reader.GetOrdinal("AuthorDecisionRowsInserted")),
            ExistingAcceptedAuthorDecisionsSkipped = reader.GetInt32(reader.GetOrdinal("ExistingAcceptedAuthorDecisionsSkipped"))
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
