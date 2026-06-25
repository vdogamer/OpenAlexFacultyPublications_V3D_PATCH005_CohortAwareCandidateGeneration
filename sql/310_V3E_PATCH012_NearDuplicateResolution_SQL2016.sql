/*
V3E PATCH012 - Near-Duplicate Publication Resolution Table
SQL Server 2016-safe.

Purpose:
  Let reviewers record a resolution for possible near-duplicate publication warnings.

This table does not delete, merge, accept, or reject publication candidates.
It records reviewer intent so warnings can be managed safely and audited.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyPublicationNearDuplicateResolution', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyPublicationNearDuplicateResolution
    (
        FacultyPublicationNearDuplicateResolutionId BIGINT IDENTITY(1,1) NOT NULL,
        FacultyPublicationCandidateId BIGINT NOT NULL,
        RelatedFacultyPublicationCandidateId BIGINT NULL,
        ResolutionStatus VARCHAR(50) NOT NULL,
        ResolutionReason NVARCHAR(MAX) NULL,
        ReviewerName NVARCHAR(256) NULL,
        IsCurrent BIT NOT NULL CONSTRAINT DF_FacultyPublicationNearDuplicateResolution_IsCurrent DEFAULT (1),
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyPublicationNearDuplicateResolution_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyPublicationNearDuplicateResolution_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyPublicationNearDuplicateResolution PRIMARY KEY CLUSTERED (FacultyPublicationNearDuplicateResolutionId),
        CONSTRAINT CK_FacultyPublicationNearDuplicateResolution_Status CHECK (ResolutionStatus IN ('DuplicateOfCandidate', 'KeepSeparate', 'SuppressWarning'))
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyPublicationNearDuplicateResolution', N'U')
      AND name = N'UX_FacultyPublicationNearDuplicateResolution_CurrentCandidate'
)
BEGIN
    CREATE UNIQUE INDEX UX_FacultyPublicationNearDuplicateResolution_CurrentCandidate
    ON openalex.FacultyPublicationNearDuplicateResolution (FacultyPublicationCandidateId)
    WHERE IsCurrent = 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyPublicationNearDuplicateResolution', N'U')
      AND name = N'IX_FacultyPublicationNearDuplicateResolution_RelatedCandidate'
)
BEGIN
    CREATE INDEX IX_FacultyPublicationNearDuplicateResolution_RelatedCandidate
    ON openalex.FacultyPublicationNearDuplicateResolution (RelatedFacultyPublicationCandidateId, IsCurrent);
END;

SELECT
    'TableReady' AS Status,
    OBJECT_ID(N'openalex.FacultyPublicationNearDuplicateResolution', N'U') AS ObjectId;
