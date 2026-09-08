/*
V3E PATCH021I — OpenAlex Fallback Identity Preview Validation
Run after the next Neurosurgery refresh attempt.
*/

SET NOCOUNT ON;

DECLARE @DepartmentId BIGINT =
(
    SELECT TOP (1)
        DepartmentId
    FROM academiconline.Department
    WHERE CanonicalName =
        N'Department of Neurosurgery'
      AND IsActive = 1
    ORDER BY DepartmentId
);

SELECT TOP (5)
    AcademicOnlineSyncRunId,
    DepartmentName,
    RunStatus,
    ScholarsDiscovered,
    ScholarsProcessed,
    IsCacheComplete,
    PublicationsProcessed,
    ScholarPagesFetched,
    PublicationPagesFetched,
    StartedAtUtc,
    CompletedAtUtc,
    SummaryText,
    ErrorText
FROM academiconline.SyncRun
WHERE DepartmentName =
    N'Department of Neurosurgery'
ORDER BY AcademicOnlineSyncRunId DESC;

SELECT
    TotalRosterMembers =
        COUNT(*),

    AcademicOnlineResolved =
        SUM(
            CASE
                WHEN AcademicOnlineAuthorId IS NOT NULL
                    THEN 1
                ELSE 0
            END),

    OpenAlexFallbackProposed =
        SUM(
            CASE
                WHEN AcademicOnlineAuthorId IS NULL
                 AND OpenAlexAuthorId IS NOT NULL
                 AND OpenAlexResolutionStatus =
                     N'ProposedUnique'
                    THEN 1
                ELSE 0
            END),

    StillWithoutAnyIdentity =
        SUM(
            CASE
                WHEN AcademicOnlineAuthorId IS NULL
                 AND OpenAlexAuthorId IS NULL
                    THEN 1
                ELSE 0
            END)
FROM academiconline.DepartmentRosterMember
WHERE DepartmentId = @DepartmentId
  AND IsActive = 1;

SELECT
    DisplayName,
    ExpectedEmail,
    AcademicOnlineAuthorId,
    ResolutionStatus,
    OpenAlexAuthorId,
    OpenAlexResolutionStatus,
    OpenAlexLastResolvedAtUtc,
    OpenAlexResolutionEvidence
FROM academiconline.DepartmentRosterMember
WHERE DepartmentId = @DepartmentId
  AND IsActive = 1
ORDER BY
    CASE
        WHEN AcademicOnlineAuthorId IS NULL
            THEN 0
        ELSE 1
    END,
    SearchPriority,
    DisplayName;

;WITH Evidence AS
(
    SELECT
        DisplayName,
        OpenAlexResolutionStatus,
        EvidenceText =
            COALESCE(
                OpenAlexResolutionEvidence,
                N'(no OpenAlex preview evidence)')
    FROM academiconline.DepartmentRosterMember
    WHERE DepartmentId = @DepartmentId
      AND IsActive = 1
      AND AcademicOnlineAuthorId IS NULL
),
Parts AS
(
    SELECT
        1 AS PartNumber,
        1 AS StartPosition
    UNION ALL SELECT 2, 1001
    UNION ALL SELECT 3, 2001
    UNION ALL SELECT 4, 3001
)
SELECT
    e.DisplayName,
    e.OpenAlexResolutionStatus,
    EvidenceLength =
        LEN(e.EvidenceText),
    p.PartNumber,
    EvidencePart =
        SUBSTRING(
            e.EvidenceText,
            p.StartPosition,
            1000)
FROM Evidence e
CROSS JOIN Parts p
WHERE p.StartPosition <=
      LEN(e.EvidenceText)
ORDER BY
    e.DisplayName,
    p.PartNumber;

SELECT
    DepartmentId,
    CanonicalName,
    RefreshEnabled,
    LastRefreshAttemptStatus,
    LastRefreshAttemptRunId,
    CurrentSnapshotRunId,
    CurrentSnapshotAtUtc
FROM academiconline.Department
WHERE DepartmentId = @DepartmentId;
