/*
V3E PATCH017B8A - Five-Faculty Work-Expansion Selection Preflight
SQL Server 2016 compatible.

Purpose
-------
Read-only preview of how many DISTINCT OpenAlex author candidates would be
selected for FacultyIdentityCandidateWorkExpansion for the controlled cohort
V3E_PATCH017B8_FIVE_FACULTY.

It compares:
  A) current/default-like selection: MinimumEvidenceScore=55, include weak=true
  B) recommended controlled pilot selection: MinimumEvidenceScore=90, include weak=false

No OpenAlex requests are made and no data is changed.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';

IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NULL
BEGIN
    RAISERROR('openalex.PilotFacultyCohort does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyIdentityCandidate', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyIdentityCandidate does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID('tempdb..#CandidateRows') IS NOT NULL DROP TABLE #CandidateRows;

CREATE TABLE #CandidateRows
(
    FacultyId BIGINT NOT NULL,
    PilotOrder INT NOT NULL,
    DisplayName NVARCHAR(512) NOT NULL,
    CandidateAuthorOpenAlexId VARCHAR(128) NOT NULL,
    BestEvidenceScore DECIMAL(9,4) NOT NULL,
    BestCandidateType VARCHAR(64) NOT NULL
);

;WITH RawRows AS
(
    SELECT
        c.FacultyId,
        p.PilotOrder,
        f.DisplayName,
        c.CandidateAuthorOpenAlexId,
        c.EvidenceScore,
        CandidateType =
            CASE
                WHEN CHARINDEX('CandidateType=', c.EvidenceSummary) > 0
                 AND CHARINDEX(';', c.EvidenceSummary, CHARINDEX('CandidateType=', c.EvidenceSummary)) > CHARINDEX('CandidateType=', c.EvidenceSummary)
                THEN SUBSTRING(
                        c.EvidenceSummary,
                        CHARINDEX('CandidateType=', c.EvidenceSummary) + LEN('CandidateType='),
                        CHARINDEX(';', c.EvidenceSummary, CHARINDEX('CandidateType=', c.EvidenceSummary)) - (CHARINDEX('CandidateType=', c.EvidenceSummary) + LEN('CandidateType='))
                     )
                ELSE '(not tagged)'
            END
    FROM openalex.PilotFacultyCohort p
    INNER JOIN openalex.Faculty f
        ON f.FacultyId = p.FacultyId
    INNER JOIN openalex.FacultyIdentityCandidate c
        ON c.FacultyId = p.FacultyId
    WHERE p.CohortName = @CohortName
      AND p.IsEnabled = 1
      AND f.IsActive = 1
),
Grouped AS
(
    SELECT
        FacultyId,
        PilotOrder,
        DisplayName,
        CandidateAuthorOpenAlexId,
        BestEvidenceScore = MAX(EvidenceScore),
        CandidateTypeRank = MAX(CASE CandidateType
            WHEN 'PrimaryIdentityCandidate' THEN 4
            WHEN 'PossibleProfileFragment' THEN 3
            WHEN 'WeakPossibleMatch' THEN 2
            WHEN 'LikelyFalsePositive' THEN 1
            ELSE 0 END)
    FROM RawRows
    GROUP BY FacultyId, PilotOrder, DisplayName, CandidateAuthorOpenAlexId
)
INSERT INTO #CandidateRows
(
    FacultyId,
    PilotOrder,
    DisplayName,
    CandidateAuthorOpenAlexId,
    BestEvidenceScore,
    BestCandidateType
)
SELECT
    FacultyId,
    PilotOrder,
    DisplayName,
    CandidateAuthorOpenAlexId,
    BestEvidenceScore,
    BestCandidateType = CASE CandidateTypeRank
        WHEN 4 THEN 'PrimaryIdentityCandidate'
        WHEN 3 THEN 'PossibleProfileFragment'
        WHEN 2 THEN 'WeakPossibleMatch'
        WHEN 1 THEN 'LikelyFalsePositive'
        ELSE '(not tagged)' END
FROM Grouped;

PRINT '1) Candidate counts by faculty under current/default-like selection';
SELECT
    PilotOrder,
    FacultyId,
    DisplayName,
    SelectedDistinctAuthorCount = COUNT(*)
FROM #CandidateRows
WHERE BestEvidenceScore >= 55
  AND BestCandidateType IN ('PrimaryIdentityCandidate', 'PossibleProfileFragment', 'WeakPossibleMatch')
GROUP BY PilotOrder, FacultyId, DisplayName
ORDER BY PilotOrder;

PRINT '2) Candidate counts by faculty under recommended controlled pilot selection';
SELECT
    PilotOrder,
    FacultyId,
    DisplayName,
    SelectedDistinctAuthorCount = COUNT(*)
FROM #CandidateRows
WHERE BestEvidenceScore >= 90
  AND BestCandidateType IN ('PrimaryIdentityCandidate', 'PossibleProfileFragment')
GROUP BY PilotOrder, FacultyId, DisplayName
ORDER BY PilotOrder;

PRINT '3) Recommended controlled candidates';
SELECT
    PilotOrder,
    FacultyId,
    DisplayName,
    CandidateAuthorOpenAlexId,
    BestEvidenceScore,
    BestCandidateType
FROM #CandidateRows
WHERE BestEvidenceScore >= 90
  AND BestCandidateType IN ('PrimaryIdentityCandidate', 'PossibleProfileFragment')
ORDER BY PilotOrder, BestEvidenceScore DESC, CandidateAuthorOpenAlexId;

PRINT '4) Preflight summary';
SELECT
    CohortName = @CohortName,
    CurrentLikeSelectedDistinctAuthors =
    (
        SELECT COUNT(*)
        FROM #CandidateRows
        WHERE BestEvidenceScore >= 55
          AND BestCandidateType IN ('PrimaryIdentityCandidate', 'PossibleProfileFragment', 'WeakPossibleMatch')
    ),
    RecommendedSelectedDistinctAuthors =
    (
        SELECT COUNT(*)
        FROM #CandidateRows
        WHERE BestEvidenceScore >= 90
          AND BestCandidateType IN ('PrimaryIdentityCandidate', 'PossibleProfileFragment')
    ),
    SafetyStatus = 'PreviewOnly_NoOpenAlexRequest_NoDataChange',
    NextAction = 'ReviewRecommendedSelectionThenRunBoundedWorkExpansion';

DROP TABLE #CandidateRows;
