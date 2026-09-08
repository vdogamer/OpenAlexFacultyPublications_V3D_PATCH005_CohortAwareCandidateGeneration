/*
V3E PATCH021E — Official Faculty Anchor Resolution
SQL Server 2016 compatible.

Run after PATCH021B.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'academiconline.DepartmentDiscoverySeed', N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.DepartmentDiscoverySeed does not exist. Apply PATCH021A/PATCH021B first.',
        16,
        1);
    RETURN;
END;

IF OBJECT_ID(N'academiconline.DepartmentDiscoveryAnchor', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.DepartmentDiscoveryAnchor
    (
        DepartmentDiscoveryAnchorId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_academiconline_DepartmentDiscoveryAnchor PRIMARY KEY,
        ParentName NVARCHAR(512) NOT NULL,
        OfficialName NVARCHAR(512) NOT NULL,
        ScholarSearchText NVARCHAR(512) NOT NULL,
        ExpectedEmail NVARCHAR(512) NULL,
        SearchPriority INT NOT NULL,
        IsActive BIT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryAnchor_IsActive DEFAULT (1),
        SourceUrl NVARCHAR(1000) NULL,
        SourceNote NVARCHAR(1000) NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryAnchor_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoveryAnchor_Updated DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_academiconline_DepartmentDiscoveryAnchor
            UNIQUE
            (
                ParentName,
                OfficialName,
                ScholarSearchText
            )
    );

    CREATE INDEX IX_academiconline_DepartmentDiscoveryAnchor_Parent
        ON academiconline.DepartmentDiscoveryAnchor
        (
            ParentName,
            IsActive,
            OfficialName,
            SearchPriority
        );
END;

DECLARE @ParentName NVARCHAR(512) = N'School of Medicine';

DECLARE @Anchor TABLE
(
    OfficialName NVARCHAR(512) NOT NULL,
    ScholarSearchText NVARCHAR(512) NOT NULL,
    ExpectedEmail NVARCHAR(512) NULL,
    SearchPriority INT NOT NULL,
    SourceUrl NVARCHAR(1000) NOT NULL,
    SourceNote NVARCHAR(1000) NOT NULL
);

/* Medicine: current department leadership / current department faculty evidence. */
INSERT INTO @Anchor
VALUES
(
    N'Medicine',
    N'Taison Bell',
    NULL,
    1,
    N'https://med.virginia.edu/dom/about/welcome-from-the-chair/',
    N'UVA Department of Medicine identifies Taison Bell as interim chair.'
),
(
    N'Medicine',
    N'Matthew Reilley',
    NULL,
    2,
    N'https://med.virginia.edu/faculty-affairs/policies-governance/uva-faculty-senate/',
    N'UVA School of Medicine Faculty Senate identifies Matthew Reilley with Department of Medicine.'
);

/* Neurosurgery: current chair plus a current department vice-chair. */
INSERT INTO @Anchor
VALUES
(
    N'Neurosurgery',
    N'Aaron S. Dumont',
    N'asd2f@virginia.edu',
    1,
    N'https://braininstitute.virginia.edu/researchers',
    N'UVA Brain Institute identifies Aaron S. Dumont as Professor and Chair of Neurosurgery and publishes asd2f@virginia.edu.'
),
(
    N'Neurosurgery',
    N'Ashok R. Asthagiri',
    NULL,
    2,
    N'https://med.virginia.edu/neurosurgery/uva-neurosurgery-faculty/',
    N'UVA Neurosurgery identifies Ashok R. Asthagiri as Professor of Neurosurgery and Department Vice-Chair.'
);

INSERT INTO academiconline.DepartmentDiscoveryAnchor
(
    ParentName,
    OfficialName,
    ScholarSearchText,
    ExpectedEmail,
    SearchPriority,
    IsActive,
    SourceUrl,
    SourceNote,
    CreatedAtUtc,
    UpdatedAtUtc
)
SELECT
    @ParentName,
    a.OfficialName,
    a.ScholarSearchText,
    a.ExpectedEmail,
    a.SearchPriority,
    1,
    a.SourceUrl,
    a.SourceNote,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM @Anchor a
WHERE NOT EXISTS
(
    SELECT 1
    FROM academiconline.DepartmentDiscoveryAnchor existing
    WHERE existing.ParentName = @ParentName
      AND existing.OfficialName = a.OfficialName
      AND existing.ScholarSearchText = a.ScholarSearchText
);

PRINT N'PATCH021E official faculty anchors are ready.';
