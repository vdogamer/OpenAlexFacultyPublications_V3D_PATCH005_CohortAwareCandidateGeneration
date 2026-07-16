/*
V3E PATCH017B - Academic Online-Guided OpenAlex Work Backfill Preview Diagnostics
SQL Server 2016-safe.
*/

SET NOCOUNT ON;

DECLARE @PreviewRunId BIGINT = NULL;

IF OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillPreviewRun', N'U') IS NULL
   OR OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillPreviewItem', N'U') IS NULL
BEGIN
    SELECT 'PATCH017B preview tables are missing. Run sql/370_V3E_PATCH017B_AcademicOnlineGuidedWorkBackfillPreview_SQL2016.sql first.' AS DiagnosticMessage;
    RETURN;
END;

IF @PreviewRunId IS NULL
BEGIN
    SELECT @PreviewRunId = MAX(AcademicOnlineWorkBackfillPreviewRunId)
    FROM openalex.AcademicOnlineWorkBackfillPreviewRun;
END;

IF @PreviewRunId IS NULL
BEGIN
    SELECT 'No PATCH017B preview run was found.' AS DiagnosticMessage;
    RETURN;
END;

PRINT '1) Selected preview run';
SELECT
    AcademicOnlineWorkBackfillPreviewRunId,
    SourceAcademicOnlineComparisonRunId,
    FacultyId,
    MaxWorks,
    SelectedWorkCount,
    ReadyToFetchCount,
    AlreadyExistsLocallyCount,
    MissingOpenAlexIdCount,
    NotFoundByOpenAlexCount,
    FetchFailedCount,
    DuplicateSnapshotRecordsSkipped,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    SummaryText,
    ErrorText,
    StartedAtUtc,
    CompletedAtUtc
FROM openalex.AcademicOnlineWorkBackfillPreviewRun
WHERE AcademicOnlineWorkBackfillPreviewRunId = @PreviewRunId;

PRINT '2) Preview status counts';
SELECT
    PreviewStatus,
    PreviewItemCount = COUNT(*)
FROM openalex.AcademicOnlineWorkBackfillPreviewItem
WHERE AcademicOnlineWorkBackfillPreviewRunId = @PreviewRunId
GROUP BY PreviewStatus
ORDER BY PreviewStatus;

PRINT '3) Detailed preview items';
SELECT
    AcademicOnlineWorkBackfillPreviewItemId,
    FacultyId,
    FacultyDisplayName,
    AcademicOnlineScholarName,
    AcademicOnlinePublicationId,
    AcademicOnlinePublicationTitle,
    AcademicOnlinePublicationYear,
    AcademicOnlineDoi,
    AcademicOnlineOpenAlexId,
    NormalizedOpenAlexId,
    SnapshotDuplicateCount,
    LocalWorkIdAtSelection,
    PreviewStatus,
    OpenAlexHttpStatusCode,
    OpenAlexResponseContentLength,
    ReturnedOpenAlexId,
    ReturnedTitle,
    ReturnedPublicationYear,
    ReturnedDoi,
    ReturnedWorkType,
    ReturnedCitedByCount,
    OpenAlexRequestUriMasked,
    ErrorText
FROM openalex.AcademicOnlineWorkBackfillPreviewItem
WHERE AcademicOnlineWorkBackfillPreviewRunId = @PreviewRunId
ORDER BY FacultyDisplayName, AcademicOnlinePublicationYear DESC, AcademicOnlinePublicationTitle;

PRINT '4) Cross-source identity checks';
SELECT
    FacultyId,
    FacultyDisplayName,
    AcademicOnlinePublicationId,
    RequestedOpenAlexId = NormalizedOpenAlexId,
    ReturnedOpenAlexId,
    IdsMatch = CASE
        WHEN ReturnedOpenAlexId IS NULL THEN NULL
        WHEN UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ReturnedOpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', '')) = NormalizedOpenAlexId THEN 1
        ELSE 0
    END,
    AcademicOnlineTitle = AcademicOnlinePublicationTitle,
    OpenAlexTitle = ReturnedTitle,
    AcademicOnlineYear = AcademicOnlinePublicationYear,
    OpenAlexYear = ReturnedPublicationYear,
    AcademicOnlineDoi,
    OpenAlexDoi = ReturnedDoi,
    PreviewStatus
FROM openalex.AcademicOnlineWorkBackfillPreviewItem
WHERE AcademicOnlineWorkBackfillPreviewRunId = @PreviewRunId
ORDER BY FacultyDisplayName, AcademicOnlinePublicationYear DESC;

PRINT '5) Safety verification: preview did not write local Work rows';
SELECT
    i.FacultyId,
    i.FacultyDisplayName,
    i.NormalizedOpenAlexId,
    i.PreviewStatus,
    LocalWorkIdNow = w.WorkId,
    SafetyStatus = CASE
        WHEN w.WorkId IS NULL THEN 'PreviewOnly_NoLocalWorkInsert'
        ELSE 'LocalWorkNowExists_CheckWhetherInsertedByAnotherJob'
    END
FROM openalex.AcademicOnlineWorkBackfillPreviewItem i
OUTER APPLY
(
    SELECT TOP (1) w.WorkId
    FROM openalex.Work w
    WHERE i.NormalizedOpenAlexId IS NOT NULL
      AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(w.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', '')) = i.NormalizedOpenAlexId
    ORDER BY w.WorkId
) w
WHERE i.AcademicOnlineWorkBackfillPreviewRunId = @PreviewRunId
ORDER BY i.FacultyDisplayName, i.NormalizedOpenAlexId;
