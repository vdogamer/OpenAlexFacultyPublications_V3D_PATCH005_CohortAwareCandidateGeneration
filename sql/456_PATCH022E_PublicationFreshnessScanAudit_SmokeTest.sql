/*
PATCH022E — Publication Freshness Scan History / Audit Smoke Test
READ ONLY.
*/

SET NOCOUNT ON;

SELECT
    ObjectName =
        N'academiconline.FacultyPublicationFreshnessScanAudit',
    ExistsFlag =
        CASE WHEN OBJECT_ID(
            N'academiconline.FacultyPublicationFreshnessScanAudit',
            N'U') IS NULL
            THEN 0 ELSE 1 END;

IF OBJECT_ID(
        N'academiconline.FacultyPublicationFreshnessScanAudit',
        N'U') IS NULL
BEGIN
    RAISERROR(
        'Run 455_PATCH022E_PublicationFreshnessScanAudit_SQL2016.sql first.',
        16,
        1);
    RETURN;
END;

SELECT
    TotalScans = COUNT_BIG(*),
    UserIdScans =
        SUM(CASE WHEN ScanType = N'UserId' THEN 1 ELSE 0 END),
    DoiScans =
        SUM(CASE WHEN ScanType = N'Doi' THEN 1 ELSE 0 END),
    SuccessfulScans =
        SUM(CASE WHEN Succeeded = 1 THEN 1 ELSE 0 END),
    FailedScans =
        SUM(CASE WHEN Succeeded = 0 THEN 1 ELSE 0 END),
    AttentionScans =
        SUM(CASE
            WHEN AttentionItemCount > 0 OR WarningCount > 0
            THEN 1 ELSE 0 END)
FROM academiconline.FacultyPublicationFreshnessScanAudit;

SELECT TOP (25)
    FacultyPublicationFreshnessScanAuditId,
    ScanType,
    RequestedValue,
    NormalizedValue,
    RequestedBy,
    StartedAtUtc,
    CompletedAtUtc,
    DurationMs,
    Succeeded,
    UvaUserId,
    FacultyDisplayName,
    DepartmentNames,
    DoiNormalized,
    OpenAlexWorkId,
    FacultyMatchCount,
    LocalResultCount,
    AcademicOnlineResultCount,
    OpenAlexResultCount,
    ComparedItemCount,
    AttentionItemCount,
    WarningCount,
    AcademicOnlineWasAttempted,
    AcademicOnlineIsAvailable,
    OpenAlexWasAttempted,
    OpenAlexIsAvailable,
    ErrorMessage
FROM academiconline.FacultyPublicationFreshnessScanAudit
ORDER BY FacultyPublicationFreshnessScanAuditId DESC;

SELECT
    RequestedBy,
    ScanCount = COUNT_BIG(*),
    LastScanAtUtc = MAX(CompletedAtUtc)
FROM academiconline.FacultyPublicationFreshnessScanAudit
GROUP BY RequestedBy
ORDER BY LastScanAtUtc DESC;

SELECT
    ScanType,
    SourcePattern =
        CASE
            WHEN AcademicOnlineWasAttempted = 1
             AND OpenAlexWasAttempted = 1
                THEN N'Academic Online + OpenAlex'
            WHEN AcademicOnlineWasAttempted = 1
                THEN N'Academic Online only'
            WHEN OpenAlexWasAttempted = 1
                THEN N'OpenAlex only'
            ELSE N'No external source attempted'
        END,
    ScanCount = COUNT_BIG(*)
FROM academiconline.FacultyPublicationFreshnessScanAudit
GROUP BY
    ScanType,
    CASE
        WHEN AcademicOnlineWasAttempted = 1
         AND OpenAlexWasAttempted = 1
            THEN N'Academic Online + OpenAlex'
        WHEN AcademicOnlineWasAttempted = 1
            THEN N'Academic Online only'
        WHEN OpenAlexWasAttempted = 1
            THEN N'OpenAlex only'
        ELSE N'No external source attempted'
    END
ORDER BY ScanType, SourcePattern;
