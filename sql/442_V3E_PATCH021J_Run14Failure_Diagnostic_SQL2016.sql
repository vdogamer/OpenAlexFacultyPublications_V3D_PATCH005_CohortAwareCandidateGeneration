/*
V3E PATCH021J — Run 14 Failure Diagnostic
Read-only. SQL Server 2016 compatible.
*/

SET NOCOUNT ON;

DECLARE @RunId BIGINT = 14;

SELECT
    AcademicOnlineSyncRunId,
    DepartmentName,
    RunStatus,
    ScholarsDiscovered,
    ScholarsProcessed,
    PublicationsProcessed,
    ScholarPagesFetched,
    PublicationPagesFetched,
    IsCacheComplete,
    StartedAtUtc,
    CompletedAtUtc,
    SummaryText,
    ErrorText
FROM academiconline.SyncRun
WHERE AcademicOnlineSyncRunId = @RunId;

SELECT
    ErrorTextLength = LEN(ErrorText),
    PartNumber = p.PartNumber,
    ErrorPart = SUBSTRING(ErrorText, p.StartPosition, 1000)
FROM academiconline.SyncRun
CROSS JOIN
(
    SELECT 1 AS PartNumber, 1 AS StartPosition
    UNION ALL SELECT 2, 1001
    UNION ALL SELECT 3, 2001
    UNION ALL SELECT 4, 3001
    UNION ALL SELECT 5, 4001
    UNION ALL SELECT 6, 5001
) p
WHERE AcademicOnlineSyncRunId = @RunId
  AND ErrorText IS NOT NULL
  AND p.StartPosition <= LEN(ErrorText)
ORDER BY p.PartNumber;

SELECT TOP (50)
    CreatedAtUtc,
    LogLevel,
    Category,
    EventName,
    Message,
    ExceptionType,
    ExceptionMessage
FROM openalex.OperationalLog
WHERE CreatedAtUtc >=
      (
          SELECT DATEADD(MINUTE, -2, StartedAtUtc)
          FROM academiconline.SyncRun
          WHERE AcademicOnlineSyncRunId = @RunId
      )
  AND CreatedAtUtc <=
      (
          SELECT DATEADD(MINUTE, 2, CompletedAtUtc)
          FROM academiconline.SyncRun
          WHERE AcademicOnlineSyncRunId = @RunId
      )
ORDER BY CreatedAtUtc DESC;

SELECT
    SourceProvider,
    RosterMembersRepresented =
        COUNT(DISTINCT DepartmentRosterMemberId),
    AttributionRows = COUNT(*),
    UniquePublications =
        COUNT(DISTINCT PublicationKey)
FROM academiconline.DepartmentRosterPublication
WHERE LastSyncRunId = @RunId
GROUP BY SourceProvider
ORDER BY SourceProvider;

SELECT TOP (100)
    rm.DisplayName,
    drp.SourceProvider,
    drp.SourceAuthorId,
    drp.PublicationKey,
    p.PublicationYear,
    p.Title,
    p.OpenAlexId,
    p.Doi
FROM academiconline.DepartmentRosterPublication drp
JOIN academiconline.DepartmentRosterMember rm
    ON rm.DepartmentRosterMemberId =
       drp.DepartmentRosterMemberId
JOIN academiconline.Publication p
    ON p.PublicationKey =
       drp.PublicationKey
WHERE drp.LastSyncRunId = @RunId
ORDER BY
    drp.SourceProvider,
    rm.DisplayName,
    p.PublicationYear DESC,
    drp.PublicationKey DESC;
