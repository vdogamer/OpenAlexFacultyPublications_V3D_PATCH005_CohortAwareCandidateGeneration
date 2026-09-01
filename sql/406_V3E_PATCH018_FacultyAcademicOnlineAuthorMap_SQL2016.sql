/*
V3E PATCH018 - Durable Faculty <-> Academic Online Author Mapping
SQL Server 2016 compatible.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'openalex.FacultyAcademicOnlineAuthorMap', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.FacultyAcademicOnlineAuthorMap
    (
        FacultyAcademicOnlineAuthorMapId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FacultyAcademicOnlineAuthorMap PRIMARY KEY,
        FacultyId BIGINT NOT NULL,
        AcademicOnlineAuthorId NVARCHAR(128) NOT NULL,
        AcademicOnlineName NVARCHAR(512) NULL,
        AcademicOnlineEmail NVARCHAR(512) NULL,
        AcademicOnlineOrcidUrl NVARCHAR(512) NULL,
        MappingSource VARCHAR(100) NOT NULL,
        EvidenceNotes NVARCHAR(MAX) NULL,
        IsCurrent BIT NOT NULL,
        VerifiedBy NVARCHAR(512) NULL,
        VerifiedAtUtc DATETIME2(7) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL
            CONSTRAINT DF_FacultyAcademicOnlineAuthorMap_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL
            CONSTRAINT DF_FacultyAcademicOnlineAuthorMap_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_FacultyAcademicOnlineAuthorMap_Faculty
            FOREIGN KEY (FacultyId)
            REFERENCES openalex.Faculty(FacultyId)
    );

    CREATE UNIQUE INDEX UX_FacultyAcademicOnlineAuthorMap_CurrentFaculty
        ON openalex.FacultyAcademicOnlineAuthorMap(FacultyId)
        WHERE IsCurrent = 1;

    CREATE UNIQUE INDEX UX_FacultyAcademicOnlineAuthorMap_CurrentAuthor
        ON openalex.FacultyAcademicOnlineAuthorMap(AcademicOnlineAuthorId)
        WHERE IsCurrent = 1;

    CREATE INDEX IX_FacultyAcademicOnlineAuthorMap_Author
        ON openalex.FacultyAcademicOnlineAuthorMap(AcademicOnlineAuthorId, FacultyId);
END;

SELECT
    FacultyAcademicOnlineAuthorMapObjectId =
        OBJECT_ID(N'openalex.FacultyAcademicOnlineAuthorMap'),
    CurrentMappingCount =
        (SELECT COUNT(*) FROM openalex.FacultyAcademicOnlineAuthorMap WHERE IsCurrent = 1),
    SetupStatus = 'Ready';
