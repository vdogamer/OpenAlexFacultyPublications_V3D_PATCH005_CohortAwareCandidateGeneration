/*
PATCH022C — Send-to-Review Smoke Test
READ ONLY.

Run after 452_PATCH022C_SendToReview_SQL2016.sql and after testing the Web
button. This script changes nothing.
*/
SET NOCOUNT ON;

SELECT
    ObjectName = N'academiconline.FacultyPublicationManualReviewSubmission',
    ExistsFlag =
        CASE
            WHEN OBJECT_ID(
                N'academiconline.FacultyPublicationManualReviewSubmission',
                N'U') IS NULL
                THEN 0
            ELSE 1
        END;

SELECT
    ObjectName = N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi',
    ExistsFlag =
        CASE
            WHEN OBJECT_ID(
                N'academiconline.usp_SubmitFacultyPublicationReviewCandidateByDoi',
                N'P') IS NULL
                THEN 0
            ELSE 1
        END;

SELECT TOP (20)
    s.FacultyPublicationManualReviewSubmissionId,
    s.FacultyPublicationReviewCandidateId,
    s.PublicationKey,
    s.FacultyKey,
    s.DepartmentId,
    s.UvaUserId,
    s.DoiNormalized,
    s.OpenAlexId,
    s.OpenAlexAuthorId,
    s.SubmissionStatus,
    s.SubmittedBy,
    s.SubmittedAtUtc,
    s.ContextDataRunId
FROM academiconline.FacultyPublicationManualReviewSubmission s
ORDER BY
    s.FacultyPublicationManualReviewSubmissionId DESC;

SELECT TOP (20)
    q.FacultyPublicationReviewCandidateId,
    q.UvaUserId,
    q.FacultyDisplayName,
    q.DepartmentName,
    q.DoiNormalized,
    q.OpenAlexId,
    q.Title,
    q.PublicationYear,
    q.RelationshipSource,
    q.ReviewStatus,
    q.DecisionStatus,
    q.IsCurrentSource
FROM academiconline.vw_FacultyPublicationReviewStatus q
WHERE q.RelationshipSource = N'ManualDoiLookup'
ORDER BY
    q.FacultyPublicationReviewCandidateId DESC;
