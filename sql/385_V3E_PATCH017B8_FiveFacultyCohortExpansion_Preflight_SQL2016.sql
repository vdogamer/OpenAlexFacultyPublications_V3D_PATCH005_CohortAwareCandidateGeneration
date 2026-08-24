/*
V3E PATCH017B8 - Five-Faculty Cohort Expansion Preflight
SQL Server 2016 compatible.

Purpose
-------
Select a controlled next cohort of five active faculty after the successful
FacultyId 16 pilot. This script is READ ONLY. It does not insert/update/delete
faculty, cohort, identity, publication-candidate, or publication-decision rows.

Selection policy
----------------
1. Exclude the proven anchor FacultyId 16.
2. Exclude known smoke-test/demo faculty rows from production cohort selection.
3. Prefer active faculty in the same department as FacultyId 16.
4. Prefer faculty who are not already enabled in an existing PilotFacultyCohort.
5. Prefer a fresh identity pipeline (no current accepted author decision yet).
6. Prefer stronger deterministic identity seeds (ORCID + claimed OpenAlex ID,
   then ORCID, then claimed OpenAlex ID, then name-only).
7. Prefer faculty without an already-accepted publication decision so that the
   next cohort exercises the full review path.
8. Return a deterministic suggested five-person cohort and a wider candidate
   pool for inspection before any cohort registration is performed.
*/

SET NOCOUNT ON;

-- Rerun safety for the current SSMS session.
IF OBJECT_ID('tempdb..#EnabledPilotFaculty') IS NOT NULL DROP TABLE #EnabledPilotFaculty;
IF OBJECT_ID('tempdb..#FacultyReadiness') IS NOT NULL DROP TABLE #FacultyReadiness;

DECLARE @AnchorFacultyId BIGINT = 16;
DECLARE @TargetCohortSize INT = 5;
DECLARE @AnchorDepartmentName NVARCHAR(512);

IF OBJECT_ID(N'openalex.Faculty', N'U') IS NULL
BEGIN
    RAISERROR('openalex.Faculty does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyAuthorMatchDecision', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyAuthorMatchDecision does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyPublicationCandidate', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyPublicationCandidate does not exist.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NULL
BEGIN
    RAISERROR('openalex.FacultyPublicationDecision does not exist.', 16, 1);
    RETURN;
END;

SELECT @AnchorDepartmentName = NULLIF(LTRIM(RTRIM(DepartmentName)), N'')
FROM openalex.Faculty
WHERE FacultyId = @AnchorFacultyId
  AND IsActive = 1;

IF @AnchorDepartmentName IS NULL
BEGIN
    RAISERROR('FacultyId 16 is missing, inactive, or has no DepartmentName. PATCH017B8 cannot anchor the next cohort safely.', 16, 1);
    RETURN;
END;

PRINT '1) PATCH017B8 anchor faculty';
SELECT
    FacultyId,
    DisplayName,
    DepartmentName,
    AppointmentTitle,
    Orcid,
    ClaimedOpenAlexAuthorId
FROM openalex.Faculty
WHERE FacultyId = @AnchorFacultyId;

PRINT '2) PilotFacultyCohort availability/schema';
SELECT
    PilotFacultyCohortExists = CASE WHEN OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NULL THEN 0 ELSE 1 END,
    AnchorDepartmentName = @AnchorDepartmentName,
    TargetCohortSize = @TargetCohortSize;

SELECT
    ColumnName = c.name,
    DataType = TYPE_NAME(c.user_type_id),
    MaxLength = c.max_length,
    IsNullable = c.is_nullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(N'openalex.PilotFacultyCohort')
ORDER BY c.column_id;

CREATE TABLE #EnabledPilotFaculty
(
    FacultyId BIGINT NOT NULL PRIMARY KEY
);

IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        INSERT INTO #EnabledPilotFaculty(FacultyId)
        SELECT DISTINCT FacultyId
        FROM openalex.PilotFacultyCohort
        WHERE IsEnabled = 1;';
END;

CREATE TABLE #FacultyReadiness
(
    FacultyId BIGINT NOT NULL PRIMARY KEY,
    DisplayName NVARCHAR(512) NOT NULL,
    DepartmentName NVARCHAR(512) NULL,
    AppointmentTitle NVARCHAR(512) NULL,
    Orcid NVARCHAR(512) NULL,
    ClaimedOpenAlexAuthorId NVARCHAR(512) NULL,
    IsSameDepartment BIT NOT NULL,
    IsAlreadyEnabledInPilotCohort BIT NOT NULL,
    HasOrcid BIT NOT NULL,
    HasClaimedOpenAlexAuthorId BIT NOT NULL,
    IdentitySeedTier INT NOT NULL,
    CurrentAcceptedAuthorIdentityCount INT NOT NULL,
    ExistingPublicationCandidateCount INT NOT NULL,
    CurrentPublicationDecisionCount INT NOT NULL,
    CurrentAcceptedPublicationDecisionCount INT NOT NULL,
    FreshIdentityPipeline BIT NOT NULL,
    FreshPublicationReviewPipeline BIT NOT NULL
);

INSERT INTO #FacultyReadiness
(
    FacultyId,
    DisplayName,
    DepartmentName,
    AppointmentTitle,
    Orcid,
    ClaimedOpenAlexAuthorId,
    IsSameDepartment,
    IsAlreadyEnabledInPilotCohort,
    HasOrcid,
    HasClaimedOpenAlexAuthorId,
    IdentitySeedTier,
    CurrentAcceptedAuthorIdentityCount,
    ExistingPublicationCandidateCount,
    CurrentPublicationDecisionCount,
    CurrentAcceptedPublicationDecisionCount,
    FreshIdentityPipeline,
    FreshPublicationReviewPipeline
)
SELECT
    f.FacultyId,
    f.DisplayName,
    f.DepartmentName,
    f.AppointmentTitle,
    f.Orcid,
    f.ClaimedOpenAlexAuthorId,
    IsSameDepartment = CONVERT(BIT, CASE WHEN NULLIF(LTRIM(RTRIM(f.DepartmentName)), N'') = @AnchorDepartmentName THEN 1 ELSE 0 END),
    IsAlreadyEnabledInPilotCohort = CONVERT(BIT, CASE WHEN ep.FacultyId IS NULL THEN 0 ELSE 1 END),
    HasOrcid = CONVERT(BIT, CASE WHEN NULLIF(LTRIM(RTRIM(f.Orcid)), N'') IS NULL THEN 0 ELSE 1 END),
    HasClaimedOpenAlexAuthorId = CONVERT(BIT, CASE WHEN NULLIF(LTRIM(RTRIM(f.ClaimedOpenAlexAuthorId)), N'') IS NULL THEN 0 ELSE 1 END),
    IdentitySeedTier =
        CASE
            WHEN NULLIF(LTRIM(RTRIM(f.Orcid)), N'') IS NOT NULL
             AND NULLIF(LTRIM(RTRIM(f.ClaimedOpenAlexAuthorId)), N'') IS NOT NULL THEN 4
            WHEN NULLIF(LTRIM(RTRIM(f.Orcid)), N'') IS NOT NULL THEN 3
            WHEN NULLIF(LTRIM(RTRIM(f.ClaimedOpenAlexAuthorId)), N'') IS NOT NULL THEN 2
            ELSE 1
        END,
    CurrentAcceptedAuthorIdentityCount = ISNULL(ad.CurrentAcceptedAuthorIdentityCount, 0),
    ExistingPublicationCandidateCount = ISNULL(pc.ExistingPublicationCandidateCount, 0),
    CurrentPublicationDecisionCount = ISNULL(pd.CurrentPublicationDecisionCount, 0),
    CurrentAcceptedPublicationDecisionCount = ISNULL(pd.CurrentAcceptedPublicationDecisionCount, 0),
    FreshIdentityPipeline = CONVERT(BIT, CASE WHEN ISNULL(ad.CurrentAcceptedAuthorIdentityCount, 0) = 0 THEN 1 ELSE 0 END),
    FreshPublicationReviewPipeline = CONVERT(BIT, CASE WHEN ISNULL(pd.CurrentPublicationDecisionCount, 0) = 0 THEN 1 ELSE 0 END)
FROM openalex.Faculty f
LEFT JOIN #EnabledPilotFaculty ep
    ON ep.FacultyId = f.FacultyId
OUTER APPLY
(
    SELECT CurrentAcceptedAuthorIdentityCount = COUNT(*)
    FROM openalex.FacultyAuthorMatchDecision d
    WHERE d.FacultyId = f.FacultyId
      AND d.DecisionStatus = 'Accepted'
      AND d.IsCurrent = 1
) ad
OUTER APPLY
(
    SELECT ExistingPublicationCandidateCount = COUNT(*)
    FROM openalex.FacultyPublicationCandidate c
    WHERE c.FacultyId = f.FacultyId
) pc
OUTER APPLY
(
    SELECT
        CurrentPublicationDecisionCount = COUNT(*),
        CurrentAcceptedPublicationDecisionCount = SUM(CASE WHEN d.DecisionStatus = 'Accepted' THEN 1 ELSE 0 END)
    FROM openalex.FacultyPublicationDecision d
    WHERE d.FacultyId = f.FacultyId
      AND d.IsCurrent = 1
) pd
WHERE f.IsActive = 1
  AND f.FacultyId <> @AnchorFacultyId
  AND LOWER(LTRIM(RTRIM(f.DisplayName))) NOT IN (N'test faculty member', N'replace me faculty')
  AND ISNULL(LOWER(LTRIM(RTRIM(f.DepartmentName))), N'') NOT IN (N'test department', N'smoke test department')
  AND ISNULL(LOWER(LTRIM(RTRIM(f.AppointmentTitle))), N'') <> N'test faculty';

PRINT '3) Wider PATCH017B8 candidate pool (top 20, test fixtures excluded)';
;WITH Ranked AS
(
    SELECT
        r.*,
        SelectionRank = ROW_NUMBER() OVER
        (
            ORDER BY
                r.IsSameDepartment DESC,
                r.IsAlreadyEnabledInPilotCohort ASC,
                r.FreshIdentityPipeline DESC,
                r.IdentitySeedTier DESC,
                r.FreshPublicationReviewPipeline DESC,
                r.CurrentAcceptedPublicationDecisionCount ASC,
                r.ExistingPublicationCandidateCount ASC,
                r.DisplayName,
                r.FacultyId
        )
    FROM #FacultyReadiness r
)
SELECT TOP (20)
    SelectionRank,
    FacultyId,
    DisplayName,
    DepartmentName,
    AppointmentTitle,
    Orcid,
    ClaimedOpenAlexAuthorId,
    IsSameDepartment,
    IsAlreadyEnabledInPilotCohort,
    IdentitySeedTier,
    IdentitySeedLabel =
        CASE IdentitySeedTier
            WHEN 4 THEN 'ORCID + ClaimedOpenAlexId'
            WHEN 3 THEN 'ORCID only'
            WHEN 2 THEN 'ClaimedOpenAlexId only'
            ELSE 'Name only'
        END,
    CurrentAcceptedAuthorIdentityCount,
    ExistingPublicationCandidateCount,
    CurrentPublicationDecisionCount,
    CurrentAcceptedPublicationDecisionCount,
    FreshIdentityPipeline,
    FreshPublicationReviewPipeline
FROM Ranked
ORDER BY SelectionRank;

PRINT '4) Suggested five-faculty cohort - READ ONLY recommendation';
;WITH Ranked AS
(
    SELECT
        r.*,
        SelectionRank = ROW_NUMBER() OVER
        (
            ORDER BY
                r.IsSameDepartment DESC,
                r.IsAlreadyEnabledInPilotCohort ASC,
                r.FreshIdentityPipeline DESC,
                r.IdentitySeedTier DESC,
                r.FreshPublicationReviewPipeline DESC,
                r.CurrentAcceptedPublicationDecisionCount ASC,
                r.ExistingPublicationCandidateCount ASC,
                r.DisplayName,
                r.FacultyId
        )
    FROM #FacultyReadiness r
),
Suggested AS
(
    SELECT TOP (@TargetCohortSize)
        PilotOrder = ROW_NUMBER() OVER (ORDER BY SelectionRank),
        *
    FROM Ranked
    ORDER BY SelectionRank
)
SELECT
    SuggestedCohortName = 'V3E_PATCH017B8_FIVE_FACULTY',
    PilotOrder,
    FacultyId,
    DisplayName,
    DepartmentName,
    AppointmentTitle,
    Orcid,
    ClaimedOpenAlexAuthorId,
    IdentitySeedLabel =
        CASE IdentitySeedTier
            WHEN 4 THEN 'ORCID + ClaimedOpenAlexId'
            WHEN 3 THEN 'ORCID only'
            WHEN 2 THEN 'ClaimedOpenAlexId only'
            ELSE 'Name only'
        END,
    CurrentAcceptedAuthorIdentityCount,
    ExistingPublicationCandidateCount,
    CurrentPublicationDecisionCount,
    FreshIdentityPipeline,
    FreshPublicationReviewPipeline,
    SelectionReason =
        CASE
            WHEN IsSameDepartment = 1 AND FreshIdentityPipeline = 1 AND IdentitySeedTier >= 3
                THEN 'Same department; fresh identity path; strong identity seed'
            WHEN IsSameDepartment = 1 AND FreshIdentityPipeline = 1
                THEN 'Same department; fresh identity path'
            WHEN IsSameDepartment = 1
                THEN 'Same department; existing identity history may be reused/revalidated'
            ELSE 'Strongest eligible real-faculty fallback outside anchor department; no same-department candidates available'
        END
FROM Suggested
ORDER BY PilotOrder;

PRINT '5) Preflight summary';
SELECT
    AnchorFacultyId = @AnchorFacultyId,
    AnchorDepartmentName = @AnchorDepartmentName,
    ActiveEligibleFacultyOutsideAnchor = (SELECT COUNT(*) FROM #FacultyReadiness),
    KnownFixtureRowsExcluded =
    (
        SELECT COUNT(*)
        FROM openalex.Faculty f
        WHERE f.IsActive = 1
          AND f.FacultyId <> @AnchorFacultyId
          AND
          (
              LOWER(LTRIM(RTRIM(f.DisplayName))) IN (N'test faculty member', N'replace me faculty')
              OR ISNULL(LOWER(LTRIM(RTRIM(f.DepartmentName))), N'') IN (N'test department', N'smoke test department')
              OR ISNULL(LOWER(LTRIM(RTRIM(f.AppointmentTitle))), N'') = N'test faculty'
          )
    ),
    SameDepartmentCandidates = (SELECT COUNT(*) FROM #FacultyReadiness WHERE IsSameDepartment = 1),
    SameDepartmentFreshIdentityCandidates = (SELECT COUNT(*) FROM #FacultyReadiness WHERE IsSameDepartment = 1 AND FreshIdentityPipeline = 1),
    StrongSeedCandidates = (SELECT COUNT(*) FROM #FacultyReadiness WHERE IdentitySeedTier >= 3),
    TargetCohortSize = @TargetCohortSize,
    SafetyStatus = 'PreviewOnly_NoCohortOrMatchingDataChanged',
    NextAction = 'ReviewSuggestedFiveThenRegisterControlledPilotCohort';

-- Clean up temp objects so the script can be rerun in the same SSMS session.
DROP TABLE #FacultyReadiness;
DROP TABLE #EnabledPilotFaculty;
