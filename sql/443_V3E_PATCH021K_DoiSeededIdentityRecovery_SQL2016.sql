/*
V3E PATCH021K — DOI-Seeded OpenAlex Identity Recovery
SQL Server 2016 compatible.

Purpose:
- Store auditable identity seeds for official FacultyRoster members.
- Seed Elisa Kucia with three independently published DOI/OpenAlex work pairs.
- Do not directly assign an OpenAlex author ID in SQL.
- The worker must prove that the seeded works converge on one author identity.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(
       N'academiconline.DepartmentRosterMember',
       N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.DepartmentRosterMember does not exist. Apply PATCH021F first.',
        16,
        1);
    RETURN;
END;

IF OBJECT_ID(
       N'academiconline.DepartmentRosterIdentitySeed',
       N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.DepartmentRosterIdentitySeed
    (
        DepartmentRosterIdentitySeedId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_academiconline_DepartmentRosterIdentitySeed
            PRIMARY KEY,

        DepartmentRosterMemberId BIGINT NOT NULL,

        SeedType NVARCHAR(30) NOT NULL,
        SeedValue NVARCHAR(512) NOT NULL,
        OpenAlexWorkId NVARCHAR(128) NULL,

        SourceReference NVARCHAR(512) NULL,
        SourceNote NVARCHAR(1000) NULL,

        SeedPriority INT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentRosterIdentitySeed_SeedPriority
            DEFAULT (100),

        IsActive BIT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentRosterIdentitySeed_IsActive
            DEFAULT (1),

        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentRosterIdentitySeed_CreatedAtUtc
            DEFAULT (SYSUTCDATETIME()),

        UpdatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentRosterIdentitySeed_UpdatedAtUtc
            DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT FK_academiconline_DepartmentRosterIdentitySeed_RosterMember
            FOREIGN KEY (DepartmentRosterMemberId)
            REFERENCES academiconline.DepartmentRosterMember(DepartmentRosterMemberId),

        CONSTRAINT CK_academiconline_DepartmentRosterIdentitySeed_SeedType
            CHECK (SeedType IN (N'DOI'))
    );

    CREATE UNIQUE INDEX UX_academiconline_DepartmentRosterIdentitySeed_Value
        ON academiconline.DepartmentRosterIdentitySeed
        (
            DepartmentRosterMemberId,
            SeedType,
            SeedValue
        );
END;

DECLARE @DepartmentId BIGINT =
(
    SELECT TOP (1)
        DepartmentId
    FROM academiconline.Department
    WHERE CanonicalName =
        N'Department of Neurosurgery'
      AND IsActive = 1
    ORDER BY DepartmentId
);

DECLARE @RosterMemberId BIGINT =
(
    SELECT TOP (1)
        DepartmentRosterMemberId
    FROM academiconline.DepartmentRosterMember
    WHERE DepartmentId = @DepartmentId
      AND IsActive = 1
      AND DisplayName = N'Elisa Kucia'
    ORDER BY DepartmentRosterMemberId
);

IF @RosterMemberId IS NULL
BEGIN
    RAISERROR(
        N'Active Department of Neurosurgery roster member Elisa Kucia was not found.',
        16,
        1);
    RETURN;
END;

DECLARE @Seeds TABLE
(
    SeedValue NVARCHAR(512) NOT NULL,
    OpenAlexWorkId NVARCHAR(128) NOT NULL,
    SourceReference NVARCHAR(512) NOT NULL,
    SourceNote NVARCHAR(1000) NOT NULL,
    SeedPriority INT NOT NULL
);

INSERT INTO @Seeds
(
    SeedValue,
    OpenAlexWorkId,
    SourceReference,
    SourceNote,
    SeedPriority
)
VALUES
(
    N'10.1227/NEU.0b013e3181fdf912',
    N'W2065806146',
    N'PubMed PMID 21099714',
    N'Elisa J Kucia; Surgical technique and outcomes in the treatment of spinal cord ependymomas: part II: myxopapillary ependymoma.',
    1
),
(
    N'10.1227/NEU.0b013e318208f181',
    N'W2021763973',
    N'PubMed PMID 21206303',
    N'Elisa J Kucia; Surgical technique and outcomes in the treatment of spinal cord ependymomas, part 1: intramedullary ependymomas.',
    2
),
(
    N'10.1016/j.wneu.2011.06.008',
    N'W1990697343',
    N'PubMed PMID 22120367',
    N'Elisa J Kucia; Neurosurgery in Tanzania: a discussion of culture, socioeconomics, and humanitarians.',
    3
);

MERGE academiconline.DepartmentRosterIdentitySeed AS target
USING
(
    SELECT
        @RosterMemberId AS DepartmentRosterMemberId,
        N'DOI' AS SeedType,
        SeedValue,
        OpenAlexWorkId,
        SourceReference,
        SourceNote,
        SeedPriority
    FROM @Seeds
) AS source
ON target.DepartmentRosterMemberId =
   source.DepartmentRosterMemberId
AND target.SeedType =
    source.SeedType
AND target.SeedValue =
    source.SeedValue

WHEN MATCHED THEN
    UPDATE SET
        OpenAlexWorkId =
            source.OpenAlexWorkId,
        SourceReference =
            source.SourceReference,
        SourceNote =
            source.SourceNote,
        SeedPriority =
            source.SeedPriority,
        IsActive = 1,
        UpdatedAtUtc =
            SYSUTCDATETIME()

WHEN NOT MATCHED THEN
    INSERT
    (
        DepartmentRosterMemberId,
        SeedType,
        SeedValue,
        OpenAlexWorkId,
        SourceReference,
        SourceNote,
        SeedPriority,
        IsActive,
        CreatedAtUtc,
        UpdatedAtUtc
    )
    VALUES
    (
        source.DepartmentRosterMemberId,
        source.SeedType,
        source.SeedValue,
        source.OpenAlexWorkId,
        source.SourceReference,
        source.SourceNote,
        source.SeedPriority,
        1,
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );

SELECT
    rm.DisplayName,
    seed.DepartmentRosterIdentitySeedId,
    seed.SeedType,
    seed.SeedValue,
    seed.OpenAlexWorkId,
    seed.SourceReference,
    seed.SourceNote,
    seed.SeedPriority,
    seed.IsActive
FROM academiconline.DepartmentRosterIdentitySeed seed
JOIN academiconline.DepartmentRosterMember rm
    ON rm.DepartmentRosterMemberId =
       seed.DepartmentRosterMemberId
WHERE seed.DepartmentRosterMemberId =
      @RosterMemberId
ORDER BY
    seed.SeedPriority,
    seed.DepartmentRosterIdentitySeedId;

PRINT N'PATCH021K DOI identity seeds are ready.';
