/*
V3E PATCH018 - Academic Online Faculty Publications HTML Diagnostics
SQL Server 2016 compatible.
*/

SET NOCOUNT ON;

DECLARE @DemoFacultyId BIGINT = 16;

PRINT '1) Durable mapping object / current mapping counts';
SELECT
    MappingObjectId = OBJECT_ID(N'openalex.FacultyAcademicOnlineAuthorMap'),
    CurrentMappingCount =
        CASE WHEN OBJECT_ID(N'openalex.FacultyAcademicOnlineAuthorMap', N'U') IS NULL
             THEN 0
             ELSE (SELECT COUNT(*) FROM openalex.FacultyAcademicOnlineAuthorMap WHERE IsCurrent = 1)
        END;

IF OBJECT_ID(N'openalex.FacultyAcademicOnlineAuthorMap', N'U') IS NULL
BEGIN
    RAISERROR('Run 406_V3E_PATCH018_FacultyAcademicOnlineAuthorMap_SQL2016.sql first.', 16, 1);
    RETURN;
END;

PRINT '2) Current demo faculty mapping';
SELECT
    m.FacultyAcademicOnlineAuthorMapId,
    m.FacultyId,
    f.DisplayName,
    f.DepartmentName,
    f.PrimaryEmail,
    f.Orcid AS FacultyOrcid,
    m.AcademicOnlineAuthorId,
    m.AcademicOnlineName,
    m.AcademicOnlineEmail,
    m.AcademicOnlineOrcidUrl,
    m.MappingSource,
    m.EvidenceNotes,
    m.VerifiedBy,
    m.VerifiedAtUtc,
    m.IsCurrent
FROM openalex.FacultyAcademicOnlineAuthorMap m
INNER JOIN openalex.Faculty f
    ON f.FacultyId = m.FacultyId
WHERE m.FacultyId = @DemoFacultyId
  AND m.IsCurrent = 1;

PRINT '3) Mapping uniqueness checks';
SELECT
    FacultyWithMultipleCurrentMappings =
    (
        SELECT COUNT(*)
        FROM
        (
            SELECT FacultyId
            FROM openalex.FacultyAcademicOnlineAuthorMap
            WHERE IsCurrent = 1
            GROUP BY FacultyId
            HAVING COUNT(*) > 1
        ) x
    ),
    AcademicOnlineAuthorMappedToMultipleCurrentFaculty =
    (
        SELECT COUNT(*)
        FROM
        (
            SELECT AcademicOnlineAuthorId
            FROM openalex.FacultyAcademicOnlineAuthorMap
            WHERE IsCurrent = 1
            GROUP BY AcademicOnlineAuthorId
            HAVING COUNT(*) > 1
        ) x
    );

PRINT '4) Demo mapping compared with latest Academic Online snapshot';
;WITH LatestSnapshot AS
(
    SELECT TOP (1)
        s.AcademicOnlineScholarSnapshotId,
        s.FacultyId,
        s.AcademicOnlineAuthorId,
        s.AcademicOnlineName,
        s.AcademicOnlineEmail,
        s.AcademicOnlineOrcidUrl,
        s.PublicationCount,
        s.SearchRankingScore,
        s.CreatedAtUtc
    FROM openalex.AcademicOnlineScholarSnapshot s
    WHERE s.FacultyId = @DemoFacultyId
      AND NULLIF(LTRIM(RTRIM(s.AcademicOnlineAuthorId)), '') IS NOT NULL
    ORDER BY s.AcademicOnlineScholarSnapshotId DESC
)
SELECT
    m.FacultyId,
    f.DisplayName,
    CurrentMappedAcademicOnlineAuthorId = m.AcademicOnlineAuthorId,
    LatestSnapshotAcademicOnlineAuthorId = s.AcademicOnlineAuthorId,
    AuthorIdMatchesLatestSnapshot =
        CASE WHEN m.AcademicOnlineAuthorId = s.AcademicOnlineAuthorId THEN 1 ELSE 0 END,
    CurrentMappedName = m.AcademicOnlineName,
    LatestSnapshotName = s.AcademicOnlineName,
    CurrentMappedEmail = m.AcademicOnlineEmail,
    LatestSnapshotEmail = s.AcademicOnlineEmail,
    CurrentMappedOrcid = m.AcademicOnlineOrcidUrl,
    LatestSnapshotOrcid = s.AcademicOnlineOrcidUrl,
    s.PublicationCount,
    s.SearchRankingScore,
    LatestSnapshotCreatedAtUtc = s.CreatedAtUtc
FROM openalex.FacultyAcademicOnlineAuthorMap m
INNER JOIN openalex.Faculty f
    ON f.FacultyId = m.FacultyId
LEFT JOIN LatestSnapshot s
    ON s.FacultyId = m.FacultyId
WHERE m.FacultyId = @DemoFacultyId
  AND m.IsCurrent = 1;

PRINT '5) PATCH018 readiness';
DECLARE @CurrentDemoMappingCount INT =
(
    SELECT COUNT(*)
    FROM openalex.FacultyAcademicOnlineAuthorMap
    WHERE FacultyId = @DemoFacultyId
      AND IsCurrent = 1
);

DECLARE @UnverifiedDemoMappingCount INT =
(
    SELECT COUNT(*)
    FROM openalex.FacultyAcademicOnlineAuthorMap
    WHERE FacultyId = @DemoFacultyId
      AND IsCurrent = 1
      AND VerifiedAtUtc IS NULL
);

SELECT
    DemoFacultyId = @DemoFacultyId,
    CurrentDemoMappingCount = @CurrentDemoMappingCount,
    UnverifiedDemoMappingCount = @UnverifiedDemoMappingCount,
    SecurityStatus = 'AcademicOnlineApiKey_RuntimeSecret_NotStoredInMapping',
    PaginationStatus = 'ClientLoopsUntilEmptyOrRepeatedPage_WithMaxPageSafeguard',
    NextAction =
        CASE
            WHEN @CurrentDemoMappingCount <> 1
                THEN 'CreateOrRepairDemoFacultyAcademicOnlineMapping'
            WHEN @UnverifiedDemoMappingCount > 0
                THEN 'VerifyDemoFacultyAcademicOnlineMapping'
            ELSE 'SetAcademicOnlineRuntimeSecret_BuildAndOpenManagerDemo'
        END;
