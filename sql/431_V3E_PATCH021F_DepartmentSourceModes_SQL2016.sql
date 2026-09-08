/*
V3E PATCH021F — Department Source Modes + Structural Exceptions
SQL Server 2016 compatible.

Run after PATCH021E.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'academiconline.Department', N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.Department does not exist. Apply PATCH019 through PATCH021 first.',
        16,
        1);
    RETURN;
END;

IF COL_LENGTH(N'academiconline.Department', N'SourceMode') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD SourceMode NVARCHAR(30) NOT NULL
            CONSTRAINT DF_academiconline_Department_SourceMode
            DEFAULT (N'DirectDepartment');
END;

IF COL_LENGTH(N'academiconline.Department', N'SourceModeNote') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD SourceModeNote NVARCHAR(1000) NULL;
END;

GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'academiconline.Department')
      AND name = N'CK_academiconline_Department_SourceMode'
)
BEGIN
    ALTER TABLE academiconline.Department
        ADD CONSTRAINT CK_academiconline_Department_SourceMode
        CHECK
        (
            SourceMode IN
            (
                N'DirectDepartment',
                N'AggregateUnits',
                N'FacultyRoster'
            )
        );
END;

IF OBJECT_ID(N'academiconline.DepartmentSourceUnit', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.DepartmentSourceUnit
    (
        DepartmentSourceUnitId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_academiconline_DepartmentSourceUnit PRIMARY KEY,
        DepartmentId BIGINT NOT NULL,
        OfficialUnitName NVARCHAR(512) NOT NULL,
        AcademicOnlineSearchName NVARCHAR(512) NOT NULL,
        SearchPriority INT NOT NULL,
        IsActive BIT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentSourceUnit_IsActive DEFAULT (1),
        SourceUrl NVARCHAR(1000) NULL,
        SourceNote NVARCHAR(1000) NULL,
        LastAttemptAtUtc DATETIME2(3) NULL,
        LastAttemptStatus NVARCHAR(30) NULL,
        LastScholarCount INT NULL,
        LastError NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentSourceUnit_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentSourceUnit_Updated DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_academiconline_DepartmentSourceUnit_Department
            FOREIGN KEY (DepartmentId)
            REFERENCES academiconline.Department(DepartmentId),
        CONSTRAINT UQ_academiconline_DepartmentSourceUnit
            UNIQUE
            (
                DepartmentId,
                OfficialUnitName,
                AcademicOnlineSearchName
            )
    );

    CREATE INDEX IX_academiconline_DepartmentSourceUnit_Department
        ON academiconline.DepartmentSourceUnit
        (
            DepartmentId,
            IsActive,
            OfficialUnitName,
            SearchPriority
        );
END;

IF OBJECT_ID(N'academiconline.DepartmentRosterMember', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.DepartmentRosterMember
    (
        DepartmentRosterMemberId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_academiconline_DepartmentRosterMember PRIMARY KEY,
        DepartmentId BIGINT NOT NULL,
        DisplayName NVARCHAR(512) NOT NULL,
        ExpectedEmail NVARCHAR(512) NULL,
        AcademicOnlineAuthorId NVARCHAR(128) NULL,
        SearchPriority INT NOT NULL,
        IsActive BIT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentRosterMember_IsActive DEFAULT (1),
        SourceUrl NVARCHAR(1000) NULL,
        SourceNote NVARCHAR(1000) NULL,
        LastResolvedAtUtc DATETIME2(3) NULL,
        ResolutionStatus NVARCHAR(30) NULL,
        ResolutionError NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentRosterMember_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentRosterMember_Updated DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_academiconline_DepartmentRosterMember_Department
            FOREIGN KEY (DepartmentId)
            REFERENCES academiconline.Department(DepartmentId),
        CONSTRAINT UQ_academiconline_DepartmentRosterMember
            UNIQUE
            (
                DepartmentId,
                DisplayName
            )
    );

    CREATE INDEX IX_academiconline_DepartmentRosterMember_Department
        ON academiconline.DepartmentRosterMember
        (
            DepartmentId,
            IsActive,
            SearchPriority,
            DisplayName
        );

    CREATE INDEX IX_academiconline_DepartmentRosterMember_Author
        ON academiconline.DepartmentRosterMember
        (
            AcademicOnlineAuthorId
        )
        WHERE AcademicOnlineAuthorId IS NOT NULL;
END;

DECLARE @DefaultFromYear INT = YEAR(SYSUTCDATETIME()) - 1;

/* Retire exact placeholder rows left by the early PATCH019 smoke test. */
UPDATE academiconline.Department
SET
    IsActive = 0,
    RefreshEnabled = 0,
    NextRefreshDueAtUtc = NULL,
    SourceModeNote =
        COALESCE(
            SourceModeNote,
            N'Retired PATCH019 smoke-test placeholder.')
WHERE SourceName IN
(
    N'PASTE EXACT DEPARTMENT NAME HERE',
    N'ACTUAL DEPARTMENT NAME'
);


IF NOT EXISTS
(
    SELECT 1
    FROM academiconline.Department
    WHERE SourceName = N'Department of Medicine'
)
BEGIN
    INSERT INTO academiconline.Department
    (
        SourceName,
        CanonicalName,
        IsActive,
        FirstSeenAtUtc,
        LastRetrievedAtUtc,
        RefreshEnabled,
        RefreshIntervalHours,
        RefreshFailureRetryMinutes,
        RefreshFromYear,
        RefreshToYear,
        RefreshVerifiedOnly,
        RefreshMaxScholars,
        SourceMode,
        SourceModeNote
    )
    VALUES
    (
        N'Department of Medicine',
        N'Department of Medicine',
        1,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        0,
        168,
        60,
        @DefaultFromYear,
        NULL,
        0,
        1000,
        N'AggregateUnits',
        N'Official UVA Department of Medicine umbrella department; membership is assembled from its Academic Online division units.'
    );
END
ELSE
BEGIN
    UPDATE academiconline.Department
    SET
        CanonicalName = N'Department of Medicine',
        IsActive = 1,
        SourceMode = N'AggregateUnits',
        SourceModeNote =
            N'Official UVA Department of Medicine umbrella department; membership is assembled from its Academic Online division units.',
        RefreshEnabled = 0,
        RefreshMaxScholars =
            CASE WHEN RefreshMaxScholars < 1000 THEN 1000 ELSE RefreshMaxScholars END
    WHERE SourceName = N'Department of Medicine';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM academiconline.Department
    WHERE SourceName = N'Department of Neurosurgery'
)
BEGIN
    INSERT INTO academiconline.Department
    (
        SourceName,
        CanonicalName,
        IsActive,
        FirstSeenAtUtc,
        LastRetrievedAtUtc,
        RefreshEnabled,
        RefreshIntervalHours,
        RefreshFailureRetryMinutes,
        RefreshFromYear,
        RefreshToYear,
        RefreshVerifiedOnly,
        RefreshMaxScholars,
        SourceMode,
        SourceModeNote
    )
    VALUES
    (
        N'Department of Neurosurgery',
        N'Department of Neurosurgery',
        1,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        0,
        168,
        60,
        @DefaultFromYear,
        NULL,
        0,
        250,
        N'FacultyRoster',
        N'Academic Online does not expose a usable Neurosurgery department node; membership is driven by the official UVA Neurosurgery faculty roster.'
    );
END
ELSE
BEGIN
    UPDATE academiconline.Department
    SET
        CanonicalName = N'Department of Neurosurgery',
        IsActive = 1,
        SourceMode = N'FacultyRoster',
        SourceModeNote =
            N'Academic Online does not expose a usable Neurosurgery department node; membership is driven by the official UVA Neurosurgery faculty roster.',
        RefreshEnabled = 0
    WHERE SourceName = N'Department of Neurosurgery';
END;

DECLARE @MedicineDepartmentId BIGINT =
(
    SELECT DepartmentId
    FROM academiconline.Department
    WHERE SourceName = N'Department of Medicine'
);

DECLARE @NeurosurgeryDepartmentId BIGINT =
(
    SELECT DepartmentId
    FROM academiconline.Department
    WHERE SourceName = N'Department of Neurosurgery'
);

DECLARE @MedicineSourceUrl NVARCHAR(1000) =
    N'https://med.virginia.edu/dom/';

DECLARE @MedicineUnit TABLE
(
    OfficialUnitName NVARCHAR(512) NOT NULL,
    SearchName NVARCHAR(512) NOT NULL,
    SearchPriority INT NOT NULL
);

INSERT INTO @MedicineUnit
VALUES
(N'Asthma, Allergy & Immunology', N'Division of Asthma, Allergy and Immunology', 1),
(N'Asthma, Allergy & Immunology', N'Division of Allergy and Clinical Immunology', 2),
(N'Asthma, Allergy & Immunology', N'Asthma, Allergy & Immunology', 3),

(N'Cardiovascular Medicine', N'Division of Cardiovascular Medicine', 1),
(N'Cardiovascular Medicine', N'Cardiovascular Medicine', 2),

(N'Endocrinology & Metabolism', N'Division of Endocrinology and Metabolism', 1),
(N'Endocrinology & Metabolism', N'Endocrinology & Metabolism', 2),

(N'Gastroenterology & Hepatology', N'Division of Gastroenterology and Hepatology', 1),
(N'Gastroenterology & Hepatology', N'Gastroenterology & Hepatology', 2),

(N'General, Geriatric & Palliative Medicine', N'Division of General, Geriatric, Palliative and Hospital Medicine', 1),
(N'General, Geriatric & Palliative Medicine', N'Division of General, Geriatric and Palliative Medicine', 2),
(N'General, Geriatric & Palliative Medicine', N'General, Geriatric & Palliative Medicine', 3),

(N'Hematology & Oncology', N'Division of Hematology and Oncology', 1),
(N'Hematology & Oncology', N'Hematology & Oncology', 2),

(N'Hospital Medicine', N'Division of General, Geriatric, Palliative and Hospital Medicine', 1),
(N'Hospital Medicine', N'Division of Hospital Medicine', 2),
(N'Hospital Medicine', N'Hospital Medicine', 3),

(N'Infectious Diseases & International Health', N'Division of Infectious Diseases and International Health', 1),
(N'Infectious Diseases & International Health', N'Infectious Diseases & International Health', 2),

(N'Nephrology', N'Division of Nephrology', 1),
(N'Nephrology', N'Nephrology', 2),

(N'Pulmonary & Critical Care Medicine', N'Division of Pulmonary and Critical Care Medicine', 1),
(N'Pulmonary & Critical Care Medicine', N'Pulmonary & Critical Care Medicine', 2),

(N'Rheumatology', N'Division of Rheumatology', 1),
(N'Rheumatology', N'Rheumatology', 2);

INSERT INTO academiconline.DepartmentSourceUnit
(
    DepartmentId,
    OfficialUnitName,
    AcademicOnlineSearchName,
    SearchPriority,
    IsActive,
    SourceUrl,
    SourceNote,
    CreatedAtUtc,
    UpdatedAtUtc
)
SELECT
    @MedicineDepartmentId,
    u.OfficialUnitName,
    u.SearchName,
    u.SearchPriority,
    1,
    @MedicineSourceUrl,
    N'Current UVA Department of Medicine division roster; Academic Online alias is validated during refresh.',
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM @MedicineUnit u
WHERE NOT EXISTS
(
    SELECT 1
    FROM academiconline.DepartmentSourceUnit existing
    WHERE existing.DepartmentId = @MedicineDepartmentId
      AND existing.OfficialUnitName = u.OfficialUnitName
      AND existing.AcademicOnlineSearchName = u.SearchName
);

DECLARE @NeurosurgerySourceUrl NVARCHAR(1000) =
    N'https://med.virginia.edu/neurosurgery/uva-neurosurgery-faculty/';

DECLARE @NeurosurgeryRoster TABLE
(
    DisplayName NVARCHAR(512) NOT NULL,
    ExpectedEmail NVARCHAR(512) NULL,
    SearchPriority INT NOT NULL
);

INSERT INTO @NeurosurgeryRoster
VALUES
(N'Ashok R. Asthagiri', NULL, 1),
(N'Andrew Carlson', NULL, 2),
(N'Michael Catalino', NULL, 3),
(N'Aaron Dumont', N'asd2f@virginia.edu', 4),
(N'W. Jeffrey Elias', NULL, 5),
(N'Paul Grabb', NULL, 6),
(N'Ryan T. Kellogg', NULL, 7),
(N'Elisa Kucia', NULL, 8),
(N'Shayan Moosa', NULL, 9),
(N'Melike Mut Askun', NULL, 10),
(N'Juan Sardi', NULL, 11),
(N'Jason Sheehan', NULL, 12),
(N'Justin Smith', NULL, 13),
(N'Heather Spader', NULL, 14),
(N'Chun-Po Yen', NULL, 15);

INSERT INTO academiconline.DepartmentRosterMember
(
    DepartmentId,
    DisplayName,
    ExpectedEmail,
    SearchPriority,
    IsActive,
    SourceUrl,
    SourceNote,
    CreatedAtUtc,
    UpdatedAtUtc
)
SELECT
    @NeurosurgeryDepartmentId,
    r.DisplayName,
    r.ExpectedEmail,
    r.SearchPriority,
    1,
    @NeurosurgerySourceUrl,
    N'Current official UVA Neurosurgery faculty roster; Academic Online identity is resolved and cached during refresh.',
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM @NeurosurgeryRoster r
WHERE NOT EXISTS
(
    SELECT 1
    FROM academiconline.DepartmentRosterMember existing
    WHERE existing.DepartmentId = @NeurosurgeryDepartmentId
      AND existing.DisplayName = r.DisplayName
);

PRINT N'PATCH021F department source modes and structural exceptions are ready.';
