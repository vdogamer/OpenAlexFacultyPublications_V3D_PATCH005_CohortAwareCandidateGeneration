/*
V3E PATCH021 — Academic Online Department Discovery + Catalog Onboarding
SQL Server 2016 compatible.

Run after PATCH020B.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'academiconline.Department', N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.Department does not exist. Apply PATCH019 through PATCH020B first.',
        16,
        1);
    RETURN;
END;

IF COL_LENGTH(N'academiconline.Department', N'AcademicOnlineDepartmentId') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD AcademicOnlineDepartmentId NVARCHAR(128) NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'AcademicOnlineSlug') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD AcademicOnlineSlug NVARCHAR(256) NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'ParentAcademicOnlineDepartmentId') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD ParentAcademicOnlineDepartmentId NVARCHAR(128) NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'ParentName') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD ParentName NVARCHAR(512) NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'ParentSlug') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD ParentSlug NVARCHAR(256) NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'DiscoveryScholarCount') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD DiscoveryScholarCount INT NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'DiscoveryFirstSeenAtUtc') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD DiscoveryFirstSeenAtUtc DATETIME2(3) NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'DiscoveryLastSeenAtUtc') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD DiscoveryLastSeenAtUtc DATETIME2(3) NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'LastDiscoveryRunId') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD LastDiscoveryRunId BIGINT NULL;
END;

GO

IF OBJECT_ID(N'academiconline.DepartmentDiscoveryRequest', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.DepartmentDiscoveryRequest
    (
        DepartmentDiscoveryRequestId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_academiconline_DepartmentDiscoveryRequest PRIMARY KEY,
        ParentName NVARCHAR(512) NOT NULL,
        RowsPerPage INT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRequest_Rows DEFAULT (100),
        MaxPages INT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRequest_MaxPages DEFAULT (100),
        RequestStatus NVARCHAR(30) NOT NULL,
        RequestedBy NVARCHAR(256) NOT NULL,
        RequestedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRequest_Requested DEFAULT (SYSUTCDATETIME()),
        StartedAtUtc DATETIME2(3) NULL,
        CompletedAtUtc DATETIME2(3) NULL,
        LeaseOwner NVARCHAR(256) NULL,
        LeaseUntilUtc DATETIME2(3) NULL,
        DiscoveryRunId BIGINT NULL,
        ErrorText NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRequest_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRequest_Updated DEFAULT (SYSUTCDATETIME())
    );

    CREATE INDEX IX_academiconline_DepartmentDiscoveryRequest_Status
        ON academiconline.DepartmentDiscoveryRequest
        (
            RequestStatus,
            LeaseUntilUtc,
            RequestedAtUtc
        );
END;

IF OBJECT_ID(N'academiconline.DepartmentDiscoveryRun', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.DepartmentDiscoveryRun
    (
        DepartmentDiscoveryRunId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_academiconline_DepartmentDiscoveryRun PRIMARY KEY,
        DepartmentDiscoveryRequestId BIGINT NOT NULL,
        ParentName NVARCHAR(512) NOT NULL,
        RunStatus NVARCHAR(30) NOT NULL,
        PagesFetched INT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRun_Pages DEFAULT (0),
        ScholarsScanned INT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRun_Scholars DEFAULT (0),
        AffiliationsObserved INT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRun_Affiliations DEFAULT (0),
        DepartmentsDiscovered INT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRun_Discovered DEFAULT (0),
        DepartmentsInserted INT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRun_Inserted DEFAULT (0),
        DepartmentsUpdated INT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRun_UpdatedCount DEFAULT (0),
        HitMaxPages BIT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRun_HitMaxPages DEFAULT (0),
        StartedBy NVARCHAR(256) NOT NULL,
        ApplicationVersion NVARCHAR(128) NOT NULL,
        SummaryText NVARCHAR(MAX) NULL,
        ErrorText NVARCHAR(MAX) NULL,
        StartedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRun_Started DEFAULT (SYSUTCDATETIME()),
        CompletedAtUtc DATETIME2(3) NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRun_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryRun_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_academiconline_DepartmentDiscoveryRun_Request
            FOREIGN KEY (DepartmentDiscoveryRequestId)
            REFERENCES academiconline.DepartmentDiscoveryRequest(DepartmentDiscoveryRequestId)
    );

    CREATE INDEX IX_academiconline_DepartmentDiscoveryRun_Started
        ON academiconline.DepartmentDiscoveryRun
        (
            StartedAtUtc DESC,
            DepartmentDiscoveryRunId DESC
        );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'academiconline.Department')
      AND name = N'IX_academiconline_Department_AcademicOnlineId'
)
BEGIN
    CREATE UNIQUE INDEX IX_academiconline_Department_AcademicOnlineId
        ON academiconline.Department(AcademicOnlineDepartmentId)
        WHERE AcademicOnlineDepartmentId IS NOT NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'academiconline.Department')
      AND name = N'IX_academiconline_Department_AcademicOnlineSlug'
)
BEGIN
    CREATE UNIQUE INDEX IX_academiconline_Department_AcademicOnlineSlug
        ON academiconline.Department(AcademicOnlineSlug)
        WHERE AcademicOnlineSlug IS NOT NULL;
END;

PRINT N'PATCH021 department discovery and catalog onboarding schema is ready.';
