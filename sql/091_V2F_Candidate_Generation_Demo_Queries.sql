/*
OpenAlex Version 2F — Candidate Generation Demo Queries
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

PRINT 'Latest candidate-generation runs';

SELECT TOP 30
    IngestionRunId,
    RunMode,
    Status,
    InstitutionFilter,
    StartedAtUtc,
    FinishedAtUtc,
    ElapsedMs,
    StartedBy,
    ApplicationVersion,
    ErrorCount,
    WarningCount
FROM openalex.IngestionRun
WHERE InstitutionFilter IN
(
    'FacultyAuthorCandidateGeneration',
    'FacultyPublicationCandidateGeneration'
)
ORDER BY IngestionRunId DESC;

PRINT 'Author candidates';

SELECT TOP 50
    c.FacultyAuthorMatchCandidateId,
    f.DisplayName AS FacultyDisplayName,
    c.AuthorDisplayName,
    c.AuthorOpenAlexId,
    c.AuthorOrcid,
    c.CandidateSource,
    c.CandidateStatus,
    c.MatchScore,
    c.NameScore,
    c.OrcidScore,
    c.InstitutionScore,
    c.EvidenceSummary,
    c.UpdatedAtUtc
FROM openalex.FacultyAuthorMatchCandidate c
JOIN openalex.Faculty f
    ON c.FacultyId = f.FacultyId
ORDER BY c.UpdatedAtUtc DESC, c.MatchScore DESC;

PRINT 'Author decisions';

SELECT TOP 50
    d.FacultyAuthorMatchDecisionId,
    f.DisplayName AS FacultyDisplayName,
    d.AuthorDisplayName,
    d.AuthorOpenAlexId,
    d.DecisionStatus,
    d.DecisionReason,
    d.DecisionSource,
    d.IsCurrent,
    d.ReviewedBy,
    d.ReviewedAtUtc,
    d.ReviewerNotes
FROM openalex.FacultyAuthorMatchDecision d
JOIN openalex.Faculty f
    ON d.FacultyId = f.FacultyId
ORDER BY d.CreatedAtUtc DESC;

PRINT 'Publication candidates';

SELECT TOP 50
    pc.FacultyPublicationCandidateId,
    f.DisplayName AS FacultyDisplayName,
    pc.WorkTitle,
    pc.PublicationYear,
    pc.WorkDoi,
    pc.AuthorDisplayName,
    pc.CandidateSource,
    pc.CandidateStatus,
    pc.MatchScore,
    pc.EvidenceSummary,
    pc.UpdatedAtUtc
FROM openalex.FacultyPublicationCandidate pc
JOIN openalex.Faculty f
    ON pc.FacultyId = f.FacultyId
ORDER BY pc.UpdatedAtUtc DESC, pc.MatchScore DESC;

PRINT 'Publication decisions';

SELECT TOP 50
    d.FacultyPublicationDecisionId,
    f.DisplayName AS FacultyDisplayName,
    d.WorkTitle,
    d.PublicationYear,
    d.WorkDoi,
    d.DecisionStatus,
    d.DecisionReason,
    d.DecisionSource,
    d.IsCurrent,
    d.ReviewedBy,
    d.ReviewedAtUtc,
    d.ReviewerNotes
FROM openalex.FacultyPublicationDecision d
JOIN openalex.Faculty f
    ON d.FacultyId = f.FacultyId
ORDER BY d.CreatedAtUtc DESC;
