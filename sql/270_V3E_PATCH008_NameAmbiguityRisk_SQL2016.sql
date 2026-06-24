/*
V3E PATCH008 - Name Ambiguity Risk Profile
SQL Server 2016-safe.

Purpose:
  Detect faculty names that are risky for OpenAlex/publication matching, especially:
    - same full name collisions
    - same first-initial + last-name collisions
    - same-department collisions
    - high-frequency surnames where first-initial matching is risky

This script creates/refreshes:
  openalex.HighRiskSurnamePattern
  openalex.FacultyNameAmbiguityProfile

Important:
  This does not accept/reject identities.
  It only creates risk evidence that PATCH008 can use to make identity triage more cautious.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'openalex.HighRiskSurnamePattern', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.HighRiskSurnamePattern
    (
        HighRiskSurnamePatternId BIGINT IDENTITY(1,1) NOT NULL,
        SurnamePattern NVARCHAR(200) NOT NULL,
        NormalizedSurnamePattern NVARCHAR(200) NOT NULL,
        RiskCategory NVARCHAR(100) NOT NULL,
        EvidenceWeight INT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HighRiskSurnamePattern_IsActive DEFAULT (1),
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_HighRiskSurnamePattern_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_HighRiskSurnamePattern_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_HighRiskSurnamePattern PRIMARY KEY CLUSTERED (HighRiskSurnamePatternId),
        CONSTRAINT CK_HighRiskSurnamePattern_EvidenceWeight CHECK (EvidenceWeight BETWEEN 0 AND 100)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.HighRiskSurnamePattern', N'U')
      AND name = N'UX_HighRiskSurnamePattern_Normalized'
)
BEGIN
    CREATE UNIQUE INDEX UX_HighRiskSurnamePattern_Normalized
    ON openalex.HighRiskSurnamePattern (NormalizedSurnamePattern);
END;

IF OBJECT_ID(N'openalex.FacultyNameAmbiguityProfile', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyNameAmbiguityProfile
    (
        FacultyNameAmbiguityProfileId BIGINT IDENTITY(1,1) NOT NULL,
        FacultyId BIGINT NOT NULL,
        FacultyDisplayName NVARCHAR(512) NULL,
        FirstName NVARCHAR(200) NULL,
        LastName NVARCHAR(200) NULL,
        NormalizedFirstName NVARCHAR(200) NULL,
        NormalizedLastName NVARCHAR(200) NULL,
        FirstInitial NVARCHAR(10) NULL,
        DepartmentName NVARCHAR(512) NULL,
        HasOrcid BIT NOT NULL,
        HasClaimedOpenAlexAuthorId BIT NOT NULL,
        IsHighRiskSurname BIT NOT NULL,
        HighRiskSurnameCategory NVARCHAR(100) NULL,
        FullNameCollisionCount INT NOT NULL,
        FirstInitialLastNameCollisionCount INT NOT NULL,
        SameDepartmentFullNameCollisionCount INT NOT NULL,
        SameDepartmentFirstInitialLastNameCollisionCount INT NOT NULL,
        RiskScore INT NOT NULL,
        NameAmbiguityRiskStatus VARCHAR(50) NOT NULL,
        RiskReason NVARCHAR(MAX) NULL,
        IsCurrent BIT NOT NULL CONSTRAINT DF_FacultyNameAmbiguityProfile_IsCurrent DEFAULT (1),
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyNameAmbiguityProfile_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FacultyNameAmbiguityProfile_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FacultyNameAmbiguityProfile PRIMARY KEY CLUSTERED (FacultyNameAmbiguityProfileId),
        CONSTRAINT CK_FacultyNameAmbiguityProfile_RiskStatus CHECK (NameAmbiguityRiskStatus IN ('Low', 'Medium', 'High', 'Critical')),
        CONSTRAINT CK_FacultyNameAmbiguityProfile_RiskScore CHECK (RiskScore BETWEEN 0 AND 100)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.FacultyNameAmbiguityProfile', N'U')
      AND name = N'UX_FacultyNameAmbiguityProfile_Current'
)
BEGIN
    CREATE UNIQUE INDEX UX_FacultyNameAmbiguityProfile_Current
    ON openalex.FacultyNameAmbiguityProfile (FacultyId)
    WHERE IsCurrent = 1;
END;

DECLARE @SurnameSeeds TABLE
(
    SurnamePattern NVARCHAR(200) NOT NULL,
    RiskCategory NVARCHAR(100) NOT NULL,
    EvidenceWeight INT NOT NULL
);

INSERT INTO @SurnameSeeds (SurnamePattern, RiskCategory, EvidenceWeight)
VALUES
(N'Wang', N'High-frequency East Asian surname', 80),
(N'Zhang', N'High-frequency East Asian surname', 80),
(N'Li', N'High-frequency East Asian surname', 85),
(N'Lee', N'High-frequency East Asian surname', 85),
(N'Liu', N'High-frequency East Asian surname', 80),
(N'Chen', N'High-frequency East Asian surname', 80),
(N'Yang', N'High-frequency East Asian surname', 75),
(N'Huang', N'High-frequency East Asian surname', 75),
(N'Wu', N'High-frequency East Asian surname', 75),
(N'Zhou', N'High-frequency East Asian surname', 75),
(N'Xu', N'High-frequency East Asian surname', 75),
(N'Sun', N'High-frequency East Asian surname', 75),
(N'Ma', N'High-frequency East Asian surname', 75),
(N'Zhu', N'High-frequency East Asian surname', 75),
(N'Lin', N'High-frequency East Asian surname', 75),
(N'Luo', N'High-frequency East Asian surname', 70),
(N'Guo', N'High-frequency East Asian surname', 70),
(N'He', N'High-frequency East Asian surname', 70),
(N'Gao', N'High-frequency East Asian surname', 70),
(N'Kim', N'High-frequency Korean surname', 85),
(N'Park', N'High-frequency Korean surname', 80),
(N'Choi', N'High-frequency Korean surname', 75),
(N'Nguyen', N'High-frequency Vietnamese surname', 85),
(N'Tran', N'High-frequency Vietnamese surname', 80),
(N'Pham', N'High-frequency Vietnamese surname', 75),
(N'Hoang', N'High-frequency Vietnamese surname', 70),
(N'Huynh', N'High-frequency Vietnamese surname', 70),
(N'Singh', N'High-frequency South Asian surname', 75),
(N'Patel', N'High-frequency South Asian surname', 80),
(N'Shah', N'High-frequency South Asian surname', 70),
(N'Khan', N'High-frequency South/Central Asian surname', 75),
(N'Ahmed', N'High-frequency surname', 70),
(N'Ali', N'High-frequency surname', 70),
(N'Mohamed', N'High-frequency surname', 70),
(N'Mohammed', N'High-frequency surname', 70);

MERGE openalex.HighRiskSurnamePattern AS target
USING
(
    SELECT
        SurnamePattern,
        LOWER(LTRIM(RTRIM(SurnamePattern))) AS NormalizedSurnamePattern,
        RiskCategory,
        EvidenceWeight
    FROM @SurnameSeeds
) AS source
ON target.NormalizedSurnamePattern = source.NormalizedSurnamePattern
WHEN MATCHED THEN
    UPDATE SET
        SurnamePattern = source.SurnamePattern,
        RiskCategory = source.RiskCategory,
        EvidenceWeight = source.EvidenceWeight,
        IsActive = 1,
        UpdatedAtUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT
    (
        SurnamePattern,
        NormalizedSurnamePattern,
        RiskCategory,
        EvidenceWeight,
        IsActive,
        CreatedAtUtc,
        UpdatedAtUtc
    )
    VALUES
    (
        source.SurnamePattern,
        source.NormalizedSurnamePattern,
        source.RiskCategory,
        source.EvidenceWeight,
        1,
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );

BEGIN TRANSACTION;

UPDATE openalex.FacultyNameAmbiguityProfile
SET IsCurrent = 0,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE IsCurrent = 1;

;WITH FacultyBase AS
(
    SELECT
        f.FacultyId,
        f.DisplayName AS FacultyDisplayName,
        f.FirstName,
        f.LastName,
        LOWER(LTRIM(RTRIM(ISNULL(f.FirstName, N'')))) AS NormalizedFirstName,
        LOWER(LTRIM(RTRIM(ISNULL(f.LastName, N'')))) AS NormalizedLastName,
        LEFT(LOWER(LTRIM(RTRIM(ISNULL(f.FirstName, N'')))), 1) AS FirstInitial,
        f.DepartmentName,
        CAST(CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(f.Orcid, N''))), N'') IS NULL THEN 0 ELSE 1 END AS BIT) AS HasOrcid,
        CAST(CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(f.ClaimedOpenAlexAuthorId, N''))), N'') IS NULL THEN 0 ELSE 1 END AS BIT) AS HasClaimedOpenAlexAuthorId,
        h.RiskCategory AS HighRiskSurnameCategory,
        h.EvidenceWeight AS HighRiskSurnameEvidenceWeight
    FROM openalex.Faculty f
    LEFT JOIN openalex.HighRiskSurnamePattern h
        ON h.IsActive = 1
       AND h.NormalizedSurnamePattern = LOWER(LTRIM(RTRIM(ISNULL(f.LastName, N''))))
    WHERE f.IsActive = 1
),
FacultyRisk AS
(
    SELECT
        fb.*,
        FullNameCollisionCount =
        (
            SELECT COUNT(*)
            FROM FacultyBase other
            WHERE other.FacultyId <> fb.FacultyId
              AND other.NormalizedFirstName = fb.NormalizedFirstName
              AND other.NormalizedLastName = fb.NormalizedLastName
              AND fb.NormalizedFirstName <> N''
              AND fb.NormalizedLastName <> N''
        ),
        FirstInitialLastNameCollisionCount =
        (
            SELECT COUNT(*)
            FROM FacultyBase other
            WHERE other.FacultyId <> fb.FacultyId
              AND other.FirstInitial = fb.FirstInitial
              AND other.NormalizedLastName = fb.NormalizedLastName
              AND fb.FirstInitial <> N''
              AND fb.NormalizedLastName <> N''
        ),
        SameDepartmentFullNameCollisionCount =
        (
            SELECT COUNT(*)
            FROM FacultyBase other
            WHERE other.FacultyId <> fb.FacultyId
              AND ISNULL(other.DepartmentName, N'') = ISNULL(fb.DepartmentName, N'')
              AND other.NormalizedFirstName = fb.NormalizedFirstName
              AND other.NormalizedLastName = fb.NormalizedLastName
              AND fb.NormalizedFirstName <> N''
              AND fb.NormalizedLastName <> N''
        ),
        SameDepartmentFirstInitialLastNameCollisionCount =
        (
            SELECT COUNT(*)
            FROM FacultyBase other
            WHERE other.FacultyId <> fb.FacultyId
              AND ISNULL(other.DepartmentName, N'') = ISNULL(fb.DepartmentName, N'')
              AND other.FirstInitial = fb.FirstInitial
              AND other.NormalizedLastName = fb.NormalizedLastName
              AND fb.FirstInitial <> N''
              AND fb.NormalizedLastName <> N''
        )
    FROM FacultyBase fb
),
Scored AS
(
    SELECT
        fr.*,
        RiskScore =
            CASE
                WHEN fr.SameDepartmentFullNameCollisionCount > 0 THEN 100
                WHEN fr.SameDepartmentFirstInitialLastNameCollisionCount > 0 THEN 95
                WHEN fr.FullNameCollisionCount > 0 THEN 90
                WHEN fr.FirstInitialLastNameCollisionCount >= 2 THEN 85
                WHEN fr.FirstInitialLastNameCollisionCount = 1 THEN 80
                WHEN fr.HighRiskSurnameEvidenceWeight >= 80 AND fr.HasOrcid = 0 AND fr.HasClaimedOpenAlexAuthorId = 0 THEN 75
                WHEN fr.HighRiskSurnameEvidenceWeight >= 70 THEN 65
                WHEN fr.HasOrcid = 0 AND fr.HasClaimedOpenAlexAuthorId = 0 THEN 50
                ELSE 20
            END
    FROM FacultyRisk fr
)
INSERT INTO openalex.FacultyNameAmbiguityProfile
(
    FacultyId,
    FacultyDisplayName,
    FirstName,
    LastName,
    NormalizedFirstName,
    NormalizedLastName,
    FirstInitial,
    DepartmentName,
    HasOrcid,
    HasClaimedOpenAlexAuthorId,
    IsHighRiskSurname,
    HighRiskSurnameCategory,
    FullNameCollisionCount,
    FirstInitialLastNameCollisionCount,
    SameDepartmentFullNameCollisionCount,
    SameDepartmentFirstInitialLastNameCollisionCount,
    RiskScore,
    NameAmbiguityRiskStatus,
    RiskReason,
    IsCurrent,
    CreatedAtUtc,
    UpdatedAtUtc
)
SELECT
    FacultyId,
    FacultyDisplayName,
    FirstName,
    LastName,
    NormalizedFirstName,
    NormalizedLastName,
    FirstInitial,
    DepartmentName,
    HasOrcid,
    HasClaimedOpenAlexAuthorId,
    CAST(CASE WHEN HighRiskSurnameCategory IS NULL THEN 0 ELSE 1 END AS BIT) AS IsHighRiskSurname,
    HighRiskSurnameCategory,
    FullNameCollisionCount,
    FirstInitialLastNameCollisionCount,
    SameDepartmentFullNameCollisionCount,
    SameDepartmentFirstInitialLastNameCollisionCount,
    RiskScore,
    CASE
        WHEN RiskScore >= 90 THEN 'Critical'
        WHEN RiskScore >= 70 THEN 'High'
        WHEN RiskScore >= 50 THEN 'Medium'
        ELSE 'Low'
    END AS NameAmbiguityRiskStatus,
    'FullNameCollisionCount=' + CONVERT(NVARCHAR(20), FullNameCollisionCount)
        + '; FirstInitialLastNameCollisionCount=' + CONVERT(NVARCHAR(20), FirstInitialLastNameCollisionCount)
        + '; SameDepartmentFullNameCollisionCount=' + CONVERT(NVARCHAR(20), SameDepartmentFullNameCollisionCount)
        + '; SameDepartmentFirstInitialLastNameCollisionCount=' + CONVERT(NVARCHAR(20), SameDepartmentFirstInitialLastNameCollisionCount)
        + '; IsHighRiskSurname=' + CASE WHEN HighRiskSurnameCategory IS NULL THEN '0' ELSE '1' END
        + '; HighRiskSurnameCategory=' + ISNULL(HighRiskSurnameCategory, '(none)')
        + '; HasOrcid=' + CONVERT(NVARCHAR(1), HasOrcid)
        + '; HasClaimedOpenAlexAuthorId=' + CONVERT(NVARCHAR(1), HasClaimedOpenAlexAuthorId) AS RiskReason,
    1,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM Scored;

COMMIT TRANSACTION;

SELECT
    FacultyId,
    FacultyDisplayName,
    FirstName,
    LastName,
    DepartmentName,
    NameAmbiguityRiskStatus,
    RiskScore,
    FullNameCollisionCount,
    FirstInitialLastNameCollisionCount,
    SameDepartmentFullNameCollisionCount,
    SameDepartmentFirstInitialLastNameCollisionCount,
    IsHighRiskSurname,
    HighRiskSurnameCategory,
    HasOrcid,
    HasClaimedOpenAlexAuthorId,
    RiskReason
FROM openalex.FacultyNameAmbiguityProfile
WHERE IsCurrent = 1
ORDER BY RiskScore DESC, FacultyDisplayName;
