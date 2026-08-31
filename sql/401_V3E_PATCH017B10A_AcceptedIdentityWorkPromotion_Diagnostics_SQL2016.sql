/*
V3E PATCH017B10A - Accepted-Identity Work Promotion Diagnostics
SQL Server 2016 compatible.
*/

SET NOCOUNT ON;

DECLARE @CohortName NVARCHAR(400) = N'V3E_PATCH017B8_FIVE_FACULTY';
DECLARE @LatestRunId BIGINT;

SELECT TOP (1)
    @LatestRunId = AcceptedIdentityWorkPromotionRunId
FROM openalex.AcceptedIdentityWorkPromotionRun
WHERE CohortName = @CohortName
ORDER BY AcceptedIdentityWorkPromotionRunId DESC;

IF @LatestRunId IS NULL
BEGIN
    RAISERROR('No PATCH017B10A promotion run was found.', 16, 1);
    RETURN;
END;

PRINT '1) Latest promotion run';
SELECT *
FROM openalex.AcceptedIdentityWorkPromotionRun
WHERE AcceptedIdentityWorkPromotionRunId = @LatestRunId;

PRINT '2) Promotion item counts';
SELECT
    PromotionStatus,
    ItemCount = COUNT(*),
    AuthorshipRows = SUM(ISNULL(AuthorshipRows, 0)),
    AuthorshipInstitutionRows = SUM(ISNULL(AuthorshipInstitutionRows, 0)),
    MatchedInstitutionRows = SUM(ISNULL(MatchedInstitutionRows, 0))
FROM openalex.AcceptedIdentityWorkPromotionItem
WHERE AcceptedIdentityWorkPromotionRunId = @LatestRunId
GROUP BY PromotionStatus
ORDER BY PromotionStatus;

PRINT '3) Normalized coverage by cohort faculty after promotion';
SELECT
    pc.PilotOrder,
    f.FacultyId,
    f.DisplayName,
    CurrentAcceptedAuthorDecisionCount =
    (
        SELECT COUNT(*)
        FROM openalex.FacultyAuthorMatchDecision d
        WHERE d.FacultyId = f.FacultyId
          AND d.DecisionStatus = 'Accepted'
          AND d.IsCurrent = 1
    ),
    DistinctAcceptedIdentityExpandedWorks =
    (
        SELECT COUNT(DISTINCT fw.WorkOpenAlexId)
        FROM openalex.FacultyIdentityCandidateWork fw
        WHERE fw.FacultyId = f.FacultyId
          AND EXISTS
          (
              SELECT 1
              FROM openalex.FacultyAuthorMatchDecision d
              WHERE d.FacultyId = fw.FacultyId
                AND d.AuthorOpenAlexId = fw.CandidateAuthorOpenAlexId
                AND d.DecisionStatus = 'Accepted'
                AND d.IsCurrent = 1
          )
    ),
    NormalizedAcceptedIdentityWorks =
    (
        SELECT COUNT(DISTINCT fw.WorkOpenAlexId)
        FROM openalex.FacultyIdentityCandidateWork fw
        INNER JOIN openalex.Work w
            ON w.OpenAlexId = fw.WorkOpenAlexId
        WHERE fw.FacultyId = f.FacultyId
          AND EXISTS
          (
              SELECT 1
              FROM openalex.FacultyAuthorMatchDecision d
              WHERE d.FacultyId = fw.FacultyId
                AND d.AuthorOpenAlexId = fw.CandidateAuthorOpenAlexId
                AND d.DecisionStatus = 'Accepted'
                AND d.IsCurrent = 1
          )
    ),
    NormalizedWorksWithMatchingAuthorship =
    (
        SELECT COUNT(DISTINCT fw.WorkOpenAlexId)
        FROM openalex.FacultyIdentityCandidateWork fw
        INNER JOIN openalex.Work w
            ON w.OpenAlexId = fw.WorkOpenAlexId
        WHERE fw.FacultyId = f.FacultyId
          AND EXISTS
          (
              SELECT 1
              FROM openalex.FacultyAuthorMatchDecision d
              INNER JOIN openalex.WorkAuthorship wa
                  ON wa.WorkId = w.WorkId
                 AND
                 (
                     wa.AuthorOpenAlexId = d.AuthorOpenAlexId
                     OR
                     (
                         d.AuthorOrcid IS NOT NULL
                         AND wa.AuthorOrcid IS NOT NULL
                         AND wa.AuthorOrcid = d.AuthorOrcid
                     )
                 )
              WHERE d.FacultyId = fw.FacultyId
                AND d.DecisionStatus = 'Accepted'
                AND d.IsCurrent = 1
          )
    )
FROM openalex.PilotFacultyCohort pc
INNER JOIN openalex.Faculty f
    ON f.FacultyId = pc.FacultyId
WHERE pc.CohortName = @CohortName
  AND pc.IsEnabled = 1
ORDER BY pc.PilotOrder;

PRINT '4) PATCH017B10A readiness summary';
DECLARE @RunStatus VARCHAR(30);
DECLARE @SelectedWorkCount INT;
DECLARE @InsertedWorkCount INT;
DECLARE @UpdatedWorkCount INT;
DECLARE @WorkRowsBefore INT;
DECLARE @WorkRowsAfter INT;
DECLARE @PublicationCandidateRowsBefore INT;
DECLARE @PublicationCandidateRowsAfter INT;
DECLARE @PublicationDecisionRowsBefore INT;
DECLARE @PublicationDecisionRowsAfter INT;

SELECT
    @RunStatus = RunStatus,
    @SelectedWorkCount = SelectedWorkCount,
    @InsertedWorkCount = InsertedWorkCount,
    @UpdatedWorkCount = UpdatedWorkCount,
    @WorkRowsBefore = WorkRowsBefore,
    @WorkRowsAfter = WorkRowsAfter,
    @PublicationCandidateRowsBefore = PublicationCandidateRowsBefore,
    @PublicationCandidateRowsAfter = PublicationCandidateRowsAfter,
    @PublicationDecisionRowsBefore = PublicationDecisionRowsBefore,
    @PublicationDecisionRowsAfter = PublicationDecisionRowsAfter
FROM openalex.AcceptedIdentityWorkPromotionRun
WHERE AcceptedIdentityWorkPromotionRunId = @LatestRunId;

DECLARE @EnabledFacultyCount INT =
(
    SELECT COUNT(DISTINCT FacultyId)
    FROM openalex.PilotFacultyCohort
    WHERE CohortName = @CohortName
      AND IsEnabled = 1
);

DECLARE @FacultyWithNormalizedMatchingWorks INT =
(
    SELECT COUNT(DISTINCT pc.FacultyId)
    FROM openalex.PilotFacultyCohort pc
    WHERE pc.CohortName = @CohortName
      AND pc.IsEnabled = 1
      AND EXISTS
      (
          SELECT 1
          FROM openalex.FacultyAuthorMatchDecision d
          INNER JOIN openalex.WorkAuthorship wa
              ON
              (
                  wa.AuthorOpenAlexId = d.AuthorOpenAlexId
                  OR
                  (
                      d.AuthorOrcid IS NOT NULL
                      AND wa.AuthorOrcid IS NOT NULL
                      AND wa.AuthorOrcid = d.AuthorOrcid
                  )
              )
          WHERE d.FacultyId = pc.FacultyId
            AND d.DecisionStatus = 'Accepted'
            AND d.IsCurrent = 1
      )
);

SELECT
    LatestPromotionRunId = @LatestRunId,
    RunStatus = @RunStatus,
    SelectedWorkCount = @SelectedWorkCount,
    InsertedWorkCount = @InsertedWorkCount,
    UpdatedWorkCount = @UpdatedWorkCount,
    WorkRowsBefore = @WorkRowsBefore,
    WorkRowsAfter = @WorkRowsAfter,
    ExpectedWorkRowsAfter = @WorkRowsBefore + ISNULL(@InsertedWorkCount, 0),
    EnabledFacultyCount = @EnabledFacultyCount,
    FacultyWithNormalizedMatchingWorks = @FacultyWithNormalizedMatchingWorks,
    PublicationCandidateRowsBefore = @PublicationCandidateRowsBefore,
    PublicationCandidateRowsAfter = @PublicationCandidateRowsAfter,
    PublicationDecisionRowsBefore = @PublicationDecisionRowsBefore,
    PublicationDecisionRowsAfter = @PublicationDecisionRowsAfter,
    SafetyStatus =
        CASE
            WHEN @PublicationCandidateRowsBefore = @PublicationCandidateRowsAfter
             AND @PublicationDecisionRowsBefore = @PublicationDecisionRowsAfter
                THEN 'LocalWorkPromotion_NoPublicationCandidateOrDecisionChange'
            ELSE 'InvestigatePublicationCountChange'
        END,
    NextAction =
        CASE
            WHEN @RunStatus = 'Completed'
             AND @FacultyWithNormalizedMatchingWorks = @EnabledFacultyCount
             AND @WorkRowsAfter = @WorkRowsBefore + ISNULL(@InsertedWorkCount, 0)
             AND @PublicationCandidateRowsBefore = @PublicationCandidateRowsAfter
             AND @PublicationDecisionRowsBefore = @PublicationDecisionRowsAfter
                THEN 'RerunPATCH017B10FiveFacultyPublicationCandidatePreview'
            WHEN @RunStatus <> 'Completed'
                THEN 'InspectPATCH017B10AWorkPromotionFailure'
            ELSE 'ReviewNormalizedWorkCoverageBeforePublicationPreview'
        END;
