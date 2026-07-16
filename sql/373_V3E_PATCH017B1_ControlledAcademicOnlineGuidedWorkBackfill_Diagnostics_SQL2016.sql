/*
V3E PATCH017B1 - Controlled Academic Online-Guided OpenAlex Work Backfill Diagnostics
SQL Server 2016-safe.
*/

SET NOCOUNT ON;

DECLARE @BackfillRunId BIGINT = NULL;

IF OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillRun', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.AcademicOnlineWorkBackfillRun. Run sql/372_V3E_PATCH017B1_ControlledAcademicOnlineGuidedWorkBackfill_SQL2016.sql first.' AS DiagnosticMessage;
    RETURN;
END;

IF OBJECT_ID(N'openalex.AcademicOnlineWorkBackfillItem', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.AcademicOnlineWorkBackfillItem. Run sql/372_V3E_PATCH017B1_ControlledAcademicOnlineGuidedWorkBackfill_SQL2016.sql first.' AS DiagnosticMessage;
    RETURN;
END;

IF @BackfillRunId IS NULL
BEGIN
    SELECT @BackfillRunId = MAX(AcademicOnlineWorkBackfillRunId)
    FROM openalex.AcademicOnlineWorkBackfillRun;
END;

IF @BackfillRunId IS NULL
BEGIN
    SELECT 'No controlled work backfill runs were found.' AS DiagnosticMessage;
    RETURN;
END;

PRINT '1) Selected controlled backfill run';
SELECT
    AcademicOnlineWorkBackfillRunId,
    SourcePreviewRunId,
    WorkIngestionRunId,
    MaxWorks,
    SelectedWorkCount,
    InsertedWorkCount,
    AlreadyExistsCount,
    InvalidPreviewItemCount,
    FailedItemCount,
    AuthorshipRows,
    AuthorshipInstitutionRows,
    MatchedInstitutionRows,
    CandidateRowsBefore,
    CandidateRowsAfter,
    DecisionRowsBefore,
    DecisionRowsAfter,
    CandidateRowsUnchanged = CASE WHEN CandidateRowsBefore = CandidateRowsAfter THEN 1 ELSE 0 END,
    DecisionRowsUnchanged = CASE WHEN DecisionRowsBefore = DecisionRowsAfter THEN 1 ELSE 0 END,
    RunStatus,
    StartedBy,
    ApplicationVersion,
    SummaryText,
    ErrorText,
    StartedAtUtc,
    CompletedAtUtc
FROM openalex.AcademicOnlineWorkBackfillRun
WHERE AcademicOnlineWorkBackfillRunId = @BackfillRunId;

PRINT '2) Execution status counts';
SELECT
    ExecutionStatus,
    ExecutionItemCount = COUNT(*)
FROM openalex.AcademicOnlineWorkBackfillItem
WHERE AcademicOnlineWorkBackfillRunId = @BackfillRunId
GROUP BY ExecutionStatus
ORDER BY ExecutionStatus;

PRINT '3) Detailed controlled backfill items';
SELECT
    i.AcademicOnlineWorkBackfillItemId,
    i.SourcePreviewItemId,
    i.FacultyId,
    i.FacultyDisplayName,
    i.AcademicOnlinePublicationId,
    i.AcademicOnlinePublicationTitle,
    i.NormalizedOpenAlexId,
    i.LocalWorkIdBefore,
    i.WorkIdAfter,
    i.WorkInserted,
    i.ExecutionStatus,
    i.AuthorshipRows,
    i.AuthorshipInstitutionRows,
    i.MatchedInstitutionRows,
    LocalWorkOpenAlexId = w.OpenAlexId,
    LocalWorkTitle = COALESCE(w.Title, w.DisplayName),
    LocalWorkDoi = w.Doi,
    LocalPublicationYear = w.PublicationYear,
    i.ErrorText
FROM openalex.AcademicOnlineWorkBackfillItem i
LEFT JOIN openalex.Work w
    ON i.WorkIdAfter = w.WorkId
WHERE i.AcademicOnlineWorkBackfillRunId = @BackfillRunId
ORDER BY i.AcademicOnlineWorkBackfillItemId;

PRINT '4) Inserted Work and child-row verification';
SELECT
    i.FacultyId,
    i.FacultyDisplayName,
    i.NormalizedOpenAlexId,
    i.WorkIdAfter,
    WorkExists = CASE WHEN w.WorkId IS NULL THEN 0 ELSE 1 END,
    WorkOpenAlexId = w.OpenAlexId,
    WorkTitle = COALESCE(w.Title, w.DisplayName),
    WorkAuthorshipRows =
    (
        SELECT COUNT(*)
        FROM openalex.WorkAuthorship wa
        WHERE wa.WorkId = i.WorkIdAfter
    ),
    WorkAuthorshipInstitutionRows =
    (
        SELECT COUNT(*)
        FROM openalex.WorkAuthorshipInstitution wai
        WHERE wai.WorkId = i.WorkIdAfter
    ),
    WorkMatchedInstitutionRows =
    (
        SELECT COUNT(*)
        FROM openalex.WorkMatchedInstitution wmi
        WHERE wmi.WorkId = i.WorkIdAfter
    ),
    i.ExecutionStatus
FROM openalex.AcademicOnlineWorkBackfillItem i
LEFT JOIN openalex.Work w
    ON i.WorkIdAfter = w.WorkId
WHERE i.AcademicOnlineWorkBackfillRunId = @BackfillRunId
ORDER BY i.FacultyDisplayName, i.NormalizedOpenAlexId;

PRINT '5) Candidate and decision safety verification';
SELECT
    r.CandidateRowsBefore,
    r.CandidateRowsAfter,
    CandidateRowsUnchanged = CASE WHEN r.CandidateRowsBefore = r.CandidateRowsAfter THEN 1 ELSE 0 END,
    r.DecisionRowsBefore,
    r.DecisionRowsAfter,
    DecisionRowsUnchanged = CASE WHEN r.DecisionRowsBefore = r.DecisionRowsAfter THEN 1 ELSE 0 END,
    SafetyStatus = CASE
        WHEN r.CandidateRowsBefore = r.CandidateRowsAfter
         AND r.DecisionRowsBefore = r.DecisionRowsAfter
            THEN 'WorkBackfillOnly_NoCandidateOrDecisionChange'
        ELSE 'UnexpectedCandidateOrDecisionCountChange'
    END
FROM openalex.AcademicOnlineWorkBackfillRun r
WHERE r.AcademicOnlineWorkBackfillRunId = @BackfillRunId;

PRINT '6) Selected preview items now present locally';
SELECT
    p.FacultyId,
    p.FacultyDisplayName,
    p.NormalizedOpenAlexId,
    p.PreviewStatus,
    LocalWorkIdNow = w.WorkId,
    LocalWorkOpenAlexId = w.OpenAlexId,
    LocalStatus = CASE WHEN w.WorkId IS NULL THEN 'StillMissingLocally' ELSE 'NowPresentLocally' END
FROM openalex.AcademicOnlineWorkBackfillPreviewItem p
JOIN openalex.AcademicOnlineWorkBackfillRun r
    ON r.SourcePreviewRunId = p.AcademicOnlineWorkBackfillPreviewRunId
LEFT JOIN openalex.Work w
    ON UPPER(REPLACE(REPLACE(LTRIM(RTRIM(w.OpenAlexId)), 'https://openalex.org/', ''), 'http://openalex.org/', '')) = p.NormalizedOpenAlexId
WHERE r.AcademicOnlineWorkBackfillRunId = @BackfillRunId
  AND p.PreviewStatus = 'ReadyToFetch'
ORDER BY p.FacultyDisplayName, p.NormalizedOpenAlexId;
