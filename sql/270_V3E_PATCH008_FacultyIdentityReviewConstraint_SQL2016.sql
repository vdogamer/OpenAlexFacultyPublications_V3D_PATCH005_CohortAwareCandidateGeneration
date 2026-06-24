/*
V3E PATCH008 - Faculty Identity Review Constraints
SQL Server 2016-safe.

Purpose:
  Allow reviewers/admins to explicitly mark specific faculty records as needing stricter
  identity evidence before an OpenAlex author can be treated as a likely identity.

Use case:
  Two faculty members can share the same or very similar names, even in the same department.
  For those specific FacultyId values, a reviewer can require stronger evidence.

This script creates the table only. It does not seed any faculty-specific constraints.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyIdentityReviewConstraint', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyIdentityReviewConstraint
    (
        FacultyIdentityReviewConstraintId BIGINT IDENTITY(1,1) NOT NULL,
        FacultyId BIGINT NOT NULL,
        RequiresVerifiedIdentifierForLikelyIdentity BIT NOT NULL CONSTRAINT DF_FacultyIdentityReviewConstraint_RequiresVerifiedIdentifier DEFAULT (0),
        RequiresManualReviewForInitialOnlyName BIT NOT NULL CONSTRAINT DF_FacultyIdentityReviewConstraint_RequiresManualReviewForInitialOnly DEFAULT (0),
        RequiresAuthorSpecificAffiliationEvidence BIT NOT NULL CONSTRAINT DF_FacultyIdentityReviewConstraint_RequiresAuthorSpecificAffiliation DEFAULT (0),
        RequiresKnownCoauthorOrTopicEvidence BIT NOT NULL CONSTRAINT DF_FacultyIdentityReviewConstraint_RequiresKnownCoauthorOrTopic DEFAULT (0),
        ConstraintReason NVARCHAR(MAX) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_FacultyIdentityReviewConstraint_IsActive DEFAULT (1),
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyIdentityReviewConstraint_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyIdentityReviewConstraint_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyIdentityReviewConstraint PRIMARY KEY CLUSTERED (FacultyIdentityReviewConstraintId)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyIdentityReviewConstraint', N'U')
      AND name = N'UX_FacultyIdentityReviewConstraint_ActiveFaculty'
)
BEGIN
    CREATE UNIQUE INDEX UX_FacultyIdentityReviewConstraint_ActiveFaculty
    ON openalex.FacultyIdentityReviewConstraint (FacultyId)
    WHERE IsActive = 1;
END;

SELECT
    'TableReady' AS Status,
    OBJECT_ID(N'openalex.FacultyIdentityReviewConstraint', N'U') AS ObjectId;
