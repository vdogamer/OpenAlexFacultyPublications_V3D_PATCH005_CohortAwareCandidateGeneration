/*
V3E PATCH021B — Resolve Remaining SOM Department Aliases
SQL Server 2016 compatible.

Run after PATCH021A (423).
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'academiconline.DepartmentDiscoverySeed', N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.DepartmentDiscoverySeed does not exist. Apply PATCH021A first.',
        16,
        1);
    RETURN;
END;

IF COL_LENGTH(
       N'academiconline.DepartmentDiscoverySeed',
       N'ValidationParentName') IS NULL
BEGIN
    ALTER TABLE academiconline.DepartmentDiscoverySeed
        ADD ValidationParentName NVARCHAR(512) NULL;
END;

GO

/*
SQL Server compiles a batch before executing ALTER TABLE. Start a new batch
so ValidationParentName is visible to the statements below.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'academiconline.DepartmentDiscoverySeed', N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.DepartmentDiscoverySeed does not exist. Apply PATCH021A first.',
        16,
        1);
    RETURN;
END;

UPDATE academiconline.DepartmentDiscoverySeed
SET
    ValidationParentName = ParentName,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE ValidationParentName IS NULL;

/*
Biomedical Engineering is a joint UVA School of Medicine / UVA Engineering
department. Keep the existing search aliases, but allow the representative
Academic Online profile to validate under either the request parent
(School of Medicine) or this alternate parent.
*/
UPDATE academiconline.DepartmentDiscoverySeed
SET
    ValidationParentName =
        N'School of Engineering and Applied Science',
    SourceNote =
        N'UVA identifies Biomedical Engineering as a joint department of the School of Medicine and UVA Engineering.',
    SourceUrl =
        N'https://engineering.virginia.edu/bme',
    UpdatedAtUtc =
        SYSUTCDATETIME()
WHERE ParentName = N'School of Medicine'
  AND OfficialName = N'Biomedical Engineering';

DECLARE @ParentName NVARCHAR(512) =
    N'School of Medicine';

DECLARE @Alias TABLE
(
    OfficialName NVARCHAR(512) NOT NULL,
    SearchName NVARCHAR(512) NOT NULL,
    DepartmentCategory NVARCHAR(50) NOT NULL,
    SearchPriority INT NOT NULL,
    SourceUrl NVARCHAR(1000) NOT NULL,
    SourceNote NVARCHAR(1000) NOT NULL
);

/* Department of Medicine may be represented in Academic Online as Internal Medicine. */
INSERT INTO @Alias
VALUES
(
    N'Medicine',
    N'Department of Internal Medicine',
    N'Clinical',
    10,
    N'https://med.virginia.edu/dom/',
    N'Targeted Academic Online alias probe for the UVA Department of Medicine.'
),
(
    N'Medicine',
    N'Internal Medicine',
    N'Clinical',
    11,
    N'https://med.virginia.edu/dom/',
    N'Targeted Academic Online alias probe for the UVA Department of Medicine.'
);

/* Neurosurgery may be represented using the specialty synonym Neurological Surgery. */
INSERT INTO @Alias
VALUES
(
    N'Neurosurgery',
    N'Department of Neurological Surgery',
    N'Clinical',
    10,
    N'https://med.virginia.edu/neurosurgery/',
    N'Targeted Academic Online alias probe for the UVA Department of Neurosurgery.'
),
(
    N'Neurosurgery',
    N'Neurological Surgery',
    N'Clinical',
    11,
    N'https://med.virginia.edu/neurosurgery/',
    N'Targeted Academic Online alias probe for the UVA Department of Neurosurgery.'
);

/* UVA uses Orthopaedic officially, while UVA Health also uses Orthopedics terminology. */
INSERT INTO @Alias
VALUES
(
    N'Orthopaedic Surgery',
    N'Department of Orthopedic Surgery',
    N'Clinical',
    10,
    N'https://med.virginia.edu/orthopaedic-surgery/',
    N'Targeted Academic Online spelling alias for Orthopaedic Surgery.'
),
(
    N'Orthopaedic Surgery',
    N'Orthopedic Surgery',
    N'Clinical',
    11,
    N'https://med.virginia.edu/orthopaedic-surgery/',
    N'Targeted Academic Online spelling alias for Orthopaedic Surgery.'
),
(
    N'Orthopaedic Surgery',
    N'Department of Orthopedics',
    N'Clinical',
    12,
    N'https://med.virginia.edu/orthopaedic-surgery/',
    N'UVA Health also uses Department of Orthopedics terminology.'
),
(
    N'Orthopaedic Surgery',
    N'Orthopedics',
    N'Clinical',
    13,
    N'https://med.virginia.edu/orthopaedic-surgery/',
    N'UVA Health also uses Orthopedics terminology.'
);

/* Official department branding includes Head and Neck Surgery. */
INSERT INTO @Alias
VALUES
(
    N'Otolaryngology',
    N'Department of Otolaryngology-Head and Neck Surgery',
    N'Clinical',
    10,
    N'https://med.virginia.edu/otolaryngology/',
    N'UVA official department branding includes Head and Neck Surgery.'
),
(
    N'Otolaryngology',
    N'Department of Otolaryngology-Head & Neck Surgery',
    N'Clinical',
    11,
    N'https://med.virginia.edu/otolaryngology/',
    N'UVA official department branding includes Head and Neck Surgery.'
),
(
    N'Otolaryngology',
    N'Otolaryngology-Head and Neck Surgery',
    N'Clinical',
    12,
    N'https://med.virginia.edu/otolaryngology/',
    N'UVA official department branding includes Head and Neck Surgery.'
),
(
    N'Otolaryngology',
    N'Otolaryngology-Head & Neck Surgery',
    N'Clinical',
    13,
    N'https://med.virginia.edu/otolaryngology/',
    N'UVA official department branding includes Head and Neck Surgery.'
);

INSERT INTO academiconline.DepartmentDiscoverySeed
(
    ParentName,
    OfficialName,
    AcademicOnlineSearchName,
    DepartmentCategory,
    SearchPriority,
    IsActive,
    SourceUrl,
    SourceNote,
    CreatedAtUtc,
    UpdatedAtUtc,
    ValidationParentName
)
SELECT
    @ParentName,
    a.OfficialName,
    a.SearchName,
    a.DepartmentCategory,
    a.SearchPriority,
    1,
    a.SourceUrl,
    a.SourceNote,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    @ParentName
FROM @Alias a
WHERE NOT EXISTS
(
    SELECT 1
    FROM academiconline.DepartmentDiscoverySeed existing
    WHERE existing.ParentName = @ParentName
      AND existing.OfficialName = a.OfficialName
      AND existing.AcademicOnlineSearchName = a.SearchName
);

PRINT N'PATCH021B targeted department aliases and joint-parent validation are ready.';
