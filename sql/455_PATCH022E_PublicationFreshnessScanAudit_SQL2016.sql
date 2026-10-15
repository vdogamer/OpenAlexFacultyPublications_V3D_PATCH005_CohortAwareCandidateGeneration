/*
PATCH022E — Publication Freshness Scan History / Audit
SQL Server 2016 compatible.

Creates a durable audit row for each UserID or DOI freshness scan initiated
from the Web application. This table records what was checked, which sources
were attempted, counts returned, warnings/errors, duration, and the signed-in
user who requested the scan.

It does NOT create or accept publication-review candidates.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'academiconline') IS NULL
BEGIN
    EXEC(N'CREATE SCHEMA academiconline AUTHORIZATION dbo;');
END;

IF OBJECT_ID(
        N'academiconline.FacultyPublicationFreshnessScanAudit',
        N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.FacultyPublicationFreshnessScanAudit
    (
        FacultyPublicationFreshnessScanAuditId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FacultyPublicationFreshnessScanAudit PRIMARY KEY,

        ScanType NVARCHAR(20) NOT NULL,
        RequestedValue NVARCHAR(1024) NOT NULL,
        NormalizedValue NVARCHAR(1024) NULL,
        RequestedBy NVARCHAR(256) NOT NULL,

        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NOT NULL,
        DurationMs BIGINT NOT NULL,

        Succeeded BIT NOT NULL,
        ErrorMessage NVARCHAR(MAX) NULL,

        UvaUserId NVARCHAR(1024) NULL,
        FacultyDisplayName NVARCHAR(1024) NULL,
        DepartmentNames NVARCHAR(MAX) NULL,

        DoiNormalized NVARCHAR(1024) NULL,
        OpenAlexWorkId NVARCHAR(256) NULL,
        FacultyMatchCount INT NOT NULL
            CONSTRAINT DF_FacultyPublicationFreshnessScanAudit_FacultyMatchCount
            DEFAULT (0),

        LocalResultCount INT NOT NULL
            CONSTRAINT DF_FacultyPublicationFreshnessScanAudit_LocalResultCount
            DEFAULT (0),
        AcademicOnlineResultCount INT NOT NULL
            CONSTRAINT DF_FacultyPublicationFreshnessScanAudit_AcademicOnlineResultCount
            DEFAULT (0),
        OpenAlexResultCount INT NOT NULL
            CONSTRAINT DF_FacultyPublicationFreshnessScanAudit_OpenAlexResultCount
            DEFAULT (0),
        ComparedItemCount INT NOT NULL
            CONSTRAINT DF_FacultyPublicationFreshnessScanAudit_ComparedItemCount
            DEFAULT (0),
        AttentionItemCount INT NOT NULL
            CONSTRAINT DF_FacultyPublicationFreshnessScanAudit_AttentionItemCount
            DEFAULT (0),
        WarningCount INT NOT NULL
            CONSTRAINT DF_FacultyPublicationFreshnessScanAudit_WarningCount
            DEFAULT (0),

        AcademicOnlineWasAttempted BIT NOT NULL
            CONSTRAINT DF_FacultyPublicationFreshnessScanAudit_AOWasAttempted
            DEFAULT (0),
        AcademicOnlineIsAvailable BIT NOT NULL
            CONSTRAINT DF_FacultyPublicationFreshnessScanAudit_AOIsAvailable
            DEFAULT (0),
        OpenAlexWasAttempted BIT NOT NULL
            CONSTRAINT DF_FacultyPublicationFreshnessScanAudit_OAWasAttempted
            DEFAULT (0),
        OpenAlexIsAvailable BIT NOT NULL
            CONSTRAINT DF_FacultyPublicationFreshnessScanAudit_OAIsAvailable
            DEFAULT (0),

        WarningsJson NVARCHAR(MAX) NULL,

        CreatedAtUtc DATETIME2(7) NOT NULL
            CONSTRAINT DF_FacultyPublicationFreshnessScanAudit_CreatedAtUtc
            DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT CK_FacultyPublicationFreshnessScanAudit_ScanType
            CHECK (ScanType IN (N'UserId', N'Doi')),

        CONSTRAINT CK_FacultyPublicationFreshnessScanAudit_NonNegativeCounts
            CHECK
            (
                DurationMs >= 0
                AND FacultyMatchCount >= 0
                AND LocalResultCount >= 0
                AND AcademicOnlineResultCount >= 0
                AND OpenAlexResultCount >= 0
                AND ComparedItemCount >= 0
                AND AttentionItemCount >= 0
                AND WarningCount >= 0
            )
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
        OBJECT_ID(N'academiconline.FacultyPublicationFreshnessScanAudit')
      AND name =
        N'IX_FacultyPublicationFreshnessScanAudit_CompletedAtUtc'
)
BEGIN
    CREATE INDEX IX_FacultyPublicationFreshnessScanAudit_CompletedAtUtc
        ON academiconline.FacultyPublicationFreshnessScanAudit
        (
            CompletedAtUtc DESC,
            FacultyPublicationFreshnessScanAuditId DESC
        )
        INCLUDE
        (
            ScanType,
            RequestedBy,
            UvaUserId,
            AttentionItemCount,
            WarningCount,
            Succeeded
        );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
        OBJECT_ID(N'academiconline.FacultyPublicationFreshnessScanAudit')
      AND name =
        N'IX_FacultyPublicationFreshnessScanAudit_UvaUserId'
)
BEGIN
    CREATE INDEX IX_FacultyPublicationFreshnessScanAudit_UvaUserId
        ON academiconline.FacultyPublicationFreshnessScanAudit
        (
            UvaUserId,
            CompletedAtUtc DESC
        )
        WHERE UvaUserId IS NOT NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
        OBJECT_ID(N'academiconline.FacultyPublicationFreshnessScanAudit')
      AND name =
        N'IX_FacultyPublicationFreshnessScanAudit_DoiNormalized'
)
BEGIN
    CREATE INDEX IX_FacultyPublicationFreshnessScanAudit_DoiNormalized
        ON academiconline.FacultyPublicationFreshnessScanAudit
        (
            DoiNormalized,
            CompletedAtUtc DESC
        )
        WHERE DoiNormalized IS NOT NULL;
END;

SELECT
    ObjectName =
        N'academiconline.FacultyPublicationFreshnessScanAudit',
    ExistsFlag =
        CASE WHEN OBJECT_ID(
            N'academiconline.FacultyPublicationFreshnessScanAudit',
            N'U') IS NULL
            THEN 0 ELSE 1 END;

SELECT
    ColumnOrdinal = c.column_id,
    ColumnName = c.name,
    DataType = t.name,
    MaxLength = c.max_length,
    IsNullable = c.is_nullable
FROM sys.columns c
JOIN sys.types t
    ON t.user_type_id = c.user_type_id
WHERE c.object_id =
    OBJECT_ID(
        N'academiconline.FacultyPublicationFreshnessScanAudit')
ORDER BY c.column_id;

PRINT N'PATCH022E scan-audit schema is ready.';
