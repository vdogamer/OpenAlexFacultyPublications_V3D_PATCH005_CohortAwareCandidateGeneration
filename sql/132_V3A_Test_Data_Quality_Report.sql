/* OpenAlex Version 3A — Test Data Quality Report */
SET NOCOUNT ON;
SELECT DB_NAME() AS CurrentDatabase;
PRINT 'Potential duplicate author candidates';
IF OBJECT_ID(N'openalex.FacultyAuthorMatchCandidate',N'U') IS NOT NULL SELECT FacultyId,AuthorOpenAlexId,CandidateSource,COUNT_BIG(*) AS CandidateCount FROM openalex.FacultyAuthorMatchCandidate GROUP BY FacultyId,AuthorOpenAlexId,CandidateSource HAVING COUNT_BIG(*)>1 ORDER BY CandidateCount DESC;
PRINT 'Potential duplicate publication candidates';
IF OBJECT_ID(N'openalex.FacultyPublicationCandidate',N'U') IS NOT NULL SELECT FacultyId,WorkId,CandidateSource,COUNT_BIG(*) AS CandidateCount FROM openalex.FacultyPublicationCandidate GROUP BY FacultyId,WorkId,CandidateSource HAVING COUNT_BIG(*)>1 ORDER BY CandidateCount DESC;
PRINT 'Accepted publication decisions missing DOI and OpenAlex ID';
IF OBJECT_ID(N'openalex.FacultyPublicationDecision',N'U') IS NOT NULL SELECT TOP 100 * FROM openalex.FacultyPublicationDecision WHERE DecisionStatus='Accepted' AND IsCurrent=1 AND NULLIF(LTRIM(RTRIM(ISNULL(WorkDoi,''))),'') IS NULL AND NULLIF(LTRIM(RTRIM(ISNULL(WorkOpenAlexId,''))),'') IS NULL ORDER BY ReviewedAtUtc DESC;
PRINT 'Authors with no last-known institution evidence';
IF OBJECT_ID(N'openalex.Author',N'U') IS NOT NULL AND OBJECT_ID(N'openalex.AuthorLastKnownInstitution',N'U') IS NOT NULL SELECT TOP 100 a.AuthorId,a.DisplayName,a.OpenAlexId,a.Orcid,a.WorksCount,a.LastSeenAtUtc FROM openalex.Author a WHERE NOT EXISTS (SELECT 1 FROM openalex.AuthorLastKnownInstitution lki WHERE lki.AuthorId=a.AuthorId) ORDER BY a.WorksCount DESC,a.AuthorId DESC;
