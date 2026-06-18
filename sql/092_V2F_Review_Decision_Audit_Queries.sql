/*
OpenAlex Version 2F — Review Decision Audit Queries
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

PRINT 'Current accepted author decisions';

SELECT TOP 100
    f.DisplayName AS FacultyDisplayName,
    f.PrimaryEmail,
    d.AuthorDisplayName,
    d.AuthorOpenAlexId,
    d.AuthorOrcid,
    d.DecisionStatus,
    d.DecisionReason,
    d.DecisionSource,
    d.ReviewedBy,
    d.ReviewedAtUtc,
    d.ReviewerNotes
FROM openalex.FacultyAuthorMatchDecision d
JOIN openalex.Faculty f
    ON d.FacultyId = f.FacultyId
WHERE d.IsCurrent = 1
  AND d.DecisionStatus = 'Accepted'
ORDER BY f.DisplayName, d.AuthorDisplayName;

PRINT 'Current accepted publication decisions';

SELECT TOP 100
    f.DisplayName AS FacultyDisplayName,
    f.PrimaryEmail,
    d.WorkTitle,
    d.PublicationYear,
    d.WorkDoi,
    d.DecisionStatus,
    d.DecisionReason,
    d.DecisionSource,
    d.ReviewedBy,
    d.ReviewedAtUtc,
    d.ReviewerNotes
FROM openalex.FacultyPublicationDecision d
JOIN openalex.Faculty f
    ON d.FacultyId = f.FacultyId
WHERE d.IsCurrent = 1
  AND d.DecisionStatus = 'Accepted'
ORDER BY f.DisplayName, d.PublicationYear DESC, d.WorkTitle;

PRINT 'Accepted publication export view';

IF OBJECT_ID(N'openalex.vw_FacultyPublicationExport', N'V') IS NOT NULL
BEGIN
    SELECT TOP 100 *
    FROM openalex.vw_FacultyPublicationExport
    ORDER BY FacultyDisplayName, PublicationYear DESC, WorkTitle;
END
