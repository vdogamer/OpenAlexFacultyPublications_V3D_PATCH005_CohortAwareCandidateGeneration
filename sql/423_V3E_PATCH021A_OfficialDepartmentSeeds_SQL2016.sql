/*
V3E PATCH021A — Official SOM Department Seeds for Academic Online Validation
SQL Server 2016 compatible.

Run after PATCH021 (421).
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'academiconline') IS NULL
BEGIN
    RAISERROR(
        N'academiconline schema does not exist. Apply PATCH019 through PATCH021 first.',
        16,
        1);
    RETURN;
END;

IF OBJECT_ID(N'academiconline.DepartmentDiscoverySeed', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.DepartmentDiscoverySeed
    (
        DepartmentDiscoverySeedId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_academiconline_DepartmentDiscoverySeed PRIMARY KEY,
        ParentName NVARCHAR(512) NOT NULL,
        OfficialName NVARCHAR(512) NOT NULL,
        AcademicOnlineSearchName NVARCHAR(512) NOT NULL,
        DepartmentCategory NVARCHAR(50) NOT NULL,
        SearchPriority INT NOT NULL,
        IsActive BIT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoverySeed_IsActive DEFAULT (1),
        SourceUrl NVARCHAR(1000) NULL,
        SourceNote NVARCHAR(1000) NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoverySeed_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentDiscoverySeed_Updated DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_academiconline_DepartmentDiscoverySeed
            UNIQUE
            (
                ParentName,
                OfficialName,
                AcademicOnlineSearchName
            )
    );

    CREATE INDEX IX_academiconline_DepartmentDiscoverySeed_Parent
        ON academiconline.DepartmentDiscoverySeed
        (
            ParentName,
            IsActive,
            DepartmentCategory,
            OfficialName,
            SearchPriority
        );
END;

DECLARE @ParentName NVARCHAR(512) = N'School of Medicine';
DECLARE @ClinicalUrl NVARCHAR(1000) =
    N'https://med.virginia.edu/clinical/';
DECLARE @ResearchUrl NVARCHAR(1000) =
    N'https://med.virginia.edu/research/';

DECLARE @Seed TABLE
(
    OfficialName NVARCHAR(512) NOT NULL,
    SearchName NVARCHAR(512) NOT NULL,
    DepartmentCategory NVARCHAR(50) NOT NULL,
    SearchPriority INT NOT NULL,
    SourceUrl NVARCHAR(1000) NOT NULL
);

/* 20 clinical departments from UVA School of Medicine's official clinical/leadership roster. */
INSERT INTO @Seed
(
    OfficialName,
    SearchName,
    DepartmentCategory,
    SearchPriority,
    SourceUrl
)
VALUES
(N'Anesthesiology', N'Department of Anesthesiology', N'Clinical', 1, @ClinicalUrl),
(N'Anesthesiology', N'Anesthesiology', N'Clinical', 2, @ClinicalUrl),

(N'Dermatology', N'Department of Dermatology', N'Clinical', 1, @ClinicalUrl),
(N'Dermatology', N'Dermatology', N'Clinical', 2, @ClinicalUrl),

(N'Emergency Medicine', N'Department of Emergency Medicine', N'Clinical', 1, @ClinicalUrl),
(N'Emergency Medicine', N'Emergency Medicine', N'Clinical', 2, @ClinicalUrl),

(N'Family Medicine', N'Department of Family Medicine', N'Clinical', 1, @ClinicalUrl),
(N'Family Medicine', N'Family Medicine', N'Clinical', 2, @ClinicalUrl),

(N'Medicine', N'Department of Medicine', N'Clinical', 1, @ClinicalUrl),
(N'Medicine', N'Medicine', N'Clinical', 2, @ClinicalUrl),

(N'Neurology', N'Department of Neurology', N'Clinical', 1, @ClinicalUrl),
(N'Neurology', N'Neurology', N'Clinical', 2, @ClinicalUrl),

(N'Neurosurgery', N'Department of Neurosurgery', N'Clinical', 1, @ClinicalUrl),
(N'Neurosurgery', N'Neurosurgery', N'Clinical', 2, @ClinicalUrl),

(N'Obstetrics and Gynecology', N'Department of Obstetrics and Gynecology', N'Clinical', 1, @ClinicalUrl),
(N'Obstetrics and Gynecology', N'Department of Obstetrics & Gynecology', N'Clinical', 2, @ClinicalUrl),
(N'Obstetrics and Gynecology', N'Obstetrics & Gynecology', N'Clinical', 3, @ClinicalUrl),

(N'Ophthalmology', N'Department of Ophthalmology', N'Clinical', 1, @ClinicalUrl),
(N'Ophthalmology', N'Ophthalmology', N'Clinical', 2, @ClinicalUrl),

(N'Orthopaedic Surgery', N'Department of Orthopaedic Surgery', N'Clinical', 1, @ClinicalUrl),
(N'Orthopaedic Surgery', N'Orthopaedic Surgery', N'Clinical', 2, @ClinicalUrl),

(N'Otolaryngology', N'Department of Otolaryngology', N'Clinical', 1, @ClinicalUrl),
(N'Otolaryngology', N'Otolaryngology', N'Clinical', 2, @ClinicalUrl),

(N'Pathology', N'Department of Pathology', N'Clinical', 1, @ClinicalUrl),
(N'Pathology', N'Pathology', N'Clinical', 2, @ClinicalUrl),

(N'Pediatrics', N'Department of Pediatrics', N'Clinical', 1, @ClinicalUrl),
(N'Pediatrics', N'Pediatrics', N'Clinical', 2, @ClinicalUrl),

(N'Physical Medicine & Rehabilitation', N'Department of Physical Medicine & Rehabilitation', N'Clinical', 1, @ClinicalUrl),
(N'Physical Medicine & Rehabilitation', N'Department of Physical Medicine and Rehabilitation', N'Clinical', 2, @ClinicalUrl),
(N'Physical Medicine & Rehabilitation', N'Physical Medicine & Rehabilitation', N'Clinical', 3, @ClinicalUrl),

(N'Plastic Surgery', N'Department of Plastic Surgery', N'Clinical', 1, @ClinicalUrl),
(N'Plastic Surgery', N'Plastic & Maxillofacial Surgery', N'Clinical', 2, @ClinicalUrl),

(N'Psychiatry and Neurobehavioral Sciences', N'Department of Psychiatry and Neurobehavioral Sciences', N'Clinical', 1, @ClinicalUrl),
(N'Psychiatry and Neurobehavioral Sciences', N'Department of Psychiatry & Neurobehavioral Sciences', N'Clinical', 2, @ClinicalUrl),

(N'Radiation Oncology', N'Department of Radiation Oncology', N'Clinical', 1, @ClinicalUrl),
(N'Radiation Oncology', N'Radiation Oncology', N'Clinical', 2, @ClinicalUrl),

(N'Radiology & Medical Imaging', N'Department of Radiology & Medical Imaging', N'Clinical', 1, @ClinicalUrl),
(N'Radiology & Medical Imaging', N'Department of Radiology and Medical Imaging', N'Clinical', 2, @ClinicalUrl),
(N'Radiology & Medical Imaging', N'Radiology & Medical Imaging', N'Clinical', 3, @ClinicalUrl),

(N'Surgery', N'Department of Surgery', N'Clinical', 1, @ClinicalUrl),
(N'Surgery', N'Surgery', N'Clinical', 2, @ClinicalUrl),

(N'Urology', N'Department of Urology', N'Clinical', 1, @ClinicalUrl),
(N'Urology', N'Urology', N'Clinical', 2, @ClinicalUrl);

/* 9 research departments from UVA School of Medicine's official research roster. */
INSERT INTO @Seed
(
    OfficialName,
    SearchName,
    DepartmentCategory,
    SearchPriority,
    SourceUrl
)
VALUES
(N'Biochemistry and Molecular Genetics', N'Department of Biochemistry and Molecular Genetics', N'Research', 1, @ResearchUrl),
(N'Biochemistry and Molecular Genetics', N'Department of Biochemistry & Molecular Genetics', N'Research', 2, @ResearchUrl),

(N'Biomedical Engineering', N'Department of Biomedical Engineering', N'Research', 1, @ResearchUrl),
(N'Biomedical Engineering', N'Biomedical Engineering', N'Research', 2, @ResearchUrl),

(N'Cell Biology', N'Department of Cell Biology', N'Research', 1, @ResearchUrl),
(N'Cell Biology', N'Cell Biology', N'Research', 2, @ResearchUrl),

(N'Genome Sciences', N'Department of Genome Sciences', N'Research', 1, @ResearchUrl),
(N'Genome Sciences', N'Department of Genomic Sciences', N'Research', 2, @ResearchUrl),
(N'Genome Sciences', N'Genome Sciences', N'Research', 3, @ResearchUrl),

(N'Microbiology, Immunology, and Cancer Biology', N'Department of Microbiology, Immunology, and Cancer Biology', N'Research', 1, @ResearchUrl),
(N'Microbiology, Immunology, and Cancer Biology', N'Department of Microbiology, Immunology, & Cancer Biology', N'Research', 2, @ResearchUrl),

(N'Molecular Physiology and Biological Physics', N'Department of Molecular Physiology and Biological Physics', N'Research', 1, @ResearchUrl),
(N'Molecular Physiology and Biological Physics', N'Department of Molecular Physiology & Biological Physics', N'Research', 2, @ResearchUrl),

(N'Neuroscience', N'Department of Neuroscience', N'Research', 1, @ResearchUrl),
(N'Neuroscience', N'Neuroscience', N'Research', 2, @ResearchUrl),

(N'Pharmacology', N'Department of Pharmacology', N'Research', 1, @ResearchUrl),
(N'Pharmacology', N'Pharmacology', N'Research', 2, @ResearchUrl),

(N'Public Health Sciences', N'Department of Public Health Sciences', N'Research', 1, @ResearchUrl),
(N'Public Health Sciences', N'Public Health Sciences', N'Research', 2, @ResearchUrl);

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
    UpdatedAtUtc
)
SELECT
    @ParentName,
    s.OfficialName,
    s.SearchName,
    s.DepartmentCategory,
    s.SearchPriority,
    1,
    s.SourceUrl,
    N'UVA School of Medicine official department roster; Academic Online name is validated at discovery time.',
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM @Seed s
WHERE NOT EXISTS
(
    SELECT 1
    FROM academiconline.DepartmentDiscoverySeed existing
    WHERE existing.ParentName = @ParentName
      AND existing.OfficialName = s.OfficialName
      AND existing.AcademicOnlineSearchName = s.SearchName
);

PRINT N'PATCH021A official School of Medicine department seeds are ready.';
