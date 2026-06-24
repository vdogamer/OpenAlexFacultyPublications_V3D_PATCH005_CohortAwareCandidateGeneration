/*
V3E PATCH008 - Faculty Identity Review Constraint Diagnostics
SQL Server 2016-safe.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyIdentityReviewConstraint', N'U') IS NULL
BEGIN
    SELECT 'Missing openalex.FacultyIdentityReviewConstraint. Run sql/270_V3E_PATCH008_FacultyIdentityReviewConstraint_SQL2016.sql first.' AS DiagnosticMessage;
    RETURN;
END;

SELECT
    c.FacultyIdentityReviewConstraintId,
    c.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    f.DepartmentName,
    c.RequiresVerifiedIdentifierForLikelyIdentity,
    c.RequiresManualReviewForInitialOnlyName,
    c.RequiresAuthorSpecificAffiliationEvidence,
    c.RequiresKnownCoauthorOrTopicEvidence,
    c.ConstraintReason,
    c.IsActive,
    c.CreatedAtUtc,
    c.UpdatedAtUtc
FROM openalex.FacultyIdentityReviewConstraint c
LEFT JOIN openalex.Faculty f
    ON c.FacultyId = f.FacultyId
ORDER BY
    c.IsActive DESC,
    f.DisplayName,
    c.FacultyId;

SELECT
    ActiveConstraintCount = SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END),
    RequiresVerifiedIdentifierCount = SUM(CASE WHEN IsActive = 1 AND RequiresVerifiedIdentifierForLikelyIdentity = 1 THEN 1 ELSE 0 END),
    RequiresManualReviewForInitialOnlyCount = SUM(CASE WHEN IsActive = 1 AND RequiresManualReviewForInitialOnlyName = 1 THEN 1 ELSE 0 END),
    RequiresAuthorSpecificAffiliationCount = SUM(CASE WHEN IsActive = 1 AND RequiresAuthorSpecificAffiliationEvidence = 1 THEN 1 ELSE 0 END),
    RequiresKnownCoauthorOrTopicCount = SUM(CASE WHEN IsActive = 1 AND RequiresKnownCoauthorOrTopicEvidence = 1 THEN 1 ELSE 0 END)
FROM openalex.FacultyIdentityReviewConstraint;
