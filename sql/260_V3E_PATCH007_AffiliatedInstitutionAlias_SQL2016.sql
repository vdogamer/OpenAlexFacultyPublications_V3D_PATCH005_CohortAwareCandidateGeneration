/*
V3E PATCH007 - Affiliated Institution Alias Table
SQL Server 2016-safe.

Purpose:
  Store institution-name aliases that should count as affiliated evidence for publication matching.
  This keeps UVA Health / hospital evidence table-driven instead of hardcoded in C#.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.AffiliatedInstitutionAlias', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AffiliatedInstitutionAlias
    (
        AffiliatedInstitutionAliasId BIGINT IDENTITY(1,1) NOT NULL,
        InstitutionGroupName NVARCHAR(200) NOT NULL,
        InstitutionDisplayName NVARCHAR(512) NOT NULL,
        NormalizedInstitutionName NVARCHAR(512) NOT NULL,
        EvidenceWeight INT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_AffiliatedInstitutionAlias_IsActive DEFAULT (1),
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AffiliatedInstitutionAlias_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AffiliatedInstitutionAlias_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AffiliatedInstitutionAlias PRIMARY KEY CLUSTERED (AffiliatedInstitutionAliasId),
        CONSTRAINT CK_AffiliatedInstitutionAlias_EvidenceWeight CHECK (EvidenceWeight BETWEEN 0 AND 100)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AffiliatedInstitutionAlias', N'U')
      AND name = N'UX_AffiliatedInstitutionAlias_Group_Normalized'
)
BEGIN
    CREATE UNIQUE INDEX UX_AffiliatedInstitutionAlias_Group_Normalized
    ON openalex.AffiliatedInstitutionAlias
    (
        InstitutionGroupName,
        NormalizedInstitutionName
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'openalex.AffiliatedInstitutionAlias', N'U')
      AND name = N'IX_AffiliatedInstitutionAlias_Active'
)
BEGIN
    CREATE INDEX IX_AffiliatedInstitutionAlias_Active
    ON openalex.AffiliatedInstitutionAlias
    (
        IsActive,
        InstitutionGroupName,
        EvidenceWeight
    );
END;

DECLARE @Aliases TABLE
(
    InstitutionGroupName NVARCHAR(200) NOT NULL,
    InstitutionDisplayName NVARCHAR(512) NOT NULL,
    EvidenceWeight INT NOT NULL
);

INSERT INTO @Aliases
(
    InstitutionGroupName,
    InstitutionDisplayName,
    EvidenceWeight
)
VALUES
(N'UVA Affiliated Institution', N'University of Virginia', 100),
(N'UVA Affiliated Institution', N'University of Virginia Health System', 95),
(N'UVA Affiliated Institution', N'University of Virginia Medical Center', 95),
(N'UVA Affiliated Institution', N'University of Virginia Hospital', 95),
(N'UVA Affiliated Institution', N'University of Virginia Children''s Hospital', 95),
(N'UVA Affiliated Institution', N'University of Virginia Children’s Hospital', 95),
(N'UVA Affiliated Institution', N'Inova Fairfax Hospital', 80),
(N'UVA Affiliated Institution', N'Western State Hospital', 80);

MERGE openalex.AffiliatedInstitutionAlias AS target
USING
(
    SELECT
        InstitutionGroupName,
        InstitutionDisplayName,
        LOWER(LTRIM(RTRIM(InstitutionDisplayName))) AS NormalizedInstitutionName,
        EvidenceWeight
    FROM @Aliases
) AS source
ON target.InstitutionGroupName = source.InstitutionGroupName
AND target.NormalizedInstitutionName = source.NormalizedInstitutionName
WHEN MATCHED THEN
    UPDATE SET
        InstitutionDisplayName = source.InstitutionDisplayName,
        EvidenceWeight = source.EvidenceWeight,
        IsActive = 1,
        UpdatedAtUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT
    (
        InstitutionGroupName,
        InstitutionDisplayName,
        NormalizedInstitutionName,
        EvidenceWeight,
        IsActive,
        CreatedAtUtc,
        UpdatedAtUtc
    )
    VALUES
    (
        source.InstitutionGroupName,
        source.InstitutionDisplayName,
        source.NormalizedInstitutionName,
        source.EvidenceWeight,
        1,
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );

SELECT
    InstitutionGroupName,
    InstitutionDisplayName,
    NormalizedInstitutionName,
    EvidenceWeight,
    IsActive
FROM openalex.AffiliatedInstitutionAlias
WHERE InstitutionGroupName = N'UVA Affiliated Institution'
ORDER BY EvidenceWeight DESC, InstitutionDisplayName;
