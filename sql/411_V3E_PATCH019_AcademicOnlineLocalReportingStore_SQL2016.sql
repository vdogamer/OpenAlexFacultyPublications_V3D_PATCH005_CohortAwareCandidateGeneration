/*
V3E PATCH019 — Academic Online Local Reporting Store
SQL Server 2016 compatible.

Purpose:
- Keep Academic Online as the authoritative upstream source.
- Persist a synchronized local reporting copy in a source-specific schema.
- Support department publication reporting without live API calls per report request.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'academiconline') IS NULL
BEGIN
    EXEC(N'CREATE SCHEMA academiconline AUTHORIZATION dbo;');
END;

IF OBJECT_ID(N'academiconline.SyncRun', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.SyncRun
    (
        AcademicOnlineSyncRunId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_academiconline_SyncRun PRIMARY KEY,
        SyncType NVARCHAR(50) NOT NULL,
        DepartmentName NVARCHAR(512) NULL,
        FromYear INT NULL,
        ToYear INT NULL,
        VerifiedOnly BIT NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_VerifiedOnly DEFAULT (0),
        RunStatus NVARCHAR(30) NOT NULL,
        ScholarsDiscovered INT NULL,
        ScholarsProcessed INT NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_Scholars DEFAULT (0),
        MaxScholarsToProcess INT NULL,
        ScholarPaginationSafeguardHit BIT NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_ScholarPaginationSafeguardHit DEFAULT (0),
        PublicationPaginationSafeguardHitCount INT NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_PublicationPaginationSafeguardHitCount DEFAULT (0),
        IsCacheComplete BIT NULL,
        SnapshotPromoted BIT NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_SnapshotPromoted DEFAULT (0),
        PublicationsProcessed INT NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_Publications DEFAULT (0),
        ScholarPagesFetched INT NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_ScholarPages DEFAULT (0),
        PublicationPagesFetched INT NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_PublicationPages DEFAULT (0),
        StartedBy NVARCHAR(256) NOT NULL,
        ApplicationVersion NVARCHAR(128) NOT NULL,
        SummaryText NVARCHAR(MAX) NULL,
        ErrorText NVARCHAR(MAX) NULL,
        StartedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_Started DEFAULT (SYSUTCDATETIME()),
        CompletedAtUtc DATETIME2(3) NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_Updated DEFAULT (SYSUTCDATETIME())
    );

    CREATE INDEX IX_academiconline_SyncRun_StatusStarted
        ON academiconline.SyncRun(RunStatus, StartedAtUtc DESC);
END;

IF OBJECT_ID(N'academiconline.Department', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.Department
    (
        DepartmentId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_academiconline_Department PRIMARY KEY,
        SourceName NVARCHAR(512) NOT NULL,
        CanonicalName NVARCHAR(512) NOT NULL,
        IsActive BIT NOT NULL
            CONSTRAINT DF_academiconline_Department_IsActive DEFAULT (1),
        FirstSeenAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_Department_FirstSeen DEFAULT (SYSUTCDATETIME()),
        LastRetrievedAtUtc DATETIME2(3) NOT NULL,
        LastSyncRunId BIGINT NULL,
        CurrentSnapshotRunId BIGINT NULL,
        CurrentSnapshotAtUtc DATETIME2(3) NULL,
        CONSTRAINT UQ_academiconline_Department_SourceName UNIQUE (SourceName)
    );
END;

IF OBJECT_ID(N'academiconline.Scholar', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.Scholar
    (
        AcademicOnlineAuthorId NVARCHAR(128) NOT NULL
            CONSTRAINT PK_academiconline_Scholar PRIMARY KEY,
        UvaUserId NVARCHAR(32) NULL,
        DisplayName NVARCHAR(512) NOT NULL,
        Email NVARCHAR(512) NULL,
        Position NVARCHAR(512) NULL,
        Institution NVARCHAR(512) NULL,
        OrcidUrl NVARCHAR(512) NULL,
        PublicationCount INT NULL,
        HasPublications BIT NULL,
        HasVerifiedPublications BIT NULL,
        RawJson NVARCHAR(MAX) NULL,
        FirstSeenAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_Scholar_FirstSeen DEFAULT (SYSUTCDATETIME()),
        LastRetrievedAtUtc DATETIME2(3) NOT NULL,
        LastSyncRunId BIGINT NULL
    );

    CREATE INDEX IX_academiconline_Scholar_UvaUserId
        ON academiconline.Scholar(UvaUserId)
        WHERE UvaUserId IS NOT NULL;

    CREATE INDEX IX_academiconline_Scholar_Email
        ON academiconline.Scholar(Email)
        WHERE Email IS NOT NULL;
END;

IF OBJECT_ID(N'academiconline.ScholarDepartment', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.ScholarDepartment
    (
        AcademicOnlineAuthorId NVARCHAR(128) NOT NULL,
        DepartmentId BIGINT NOT NULL,
        FirstSeenAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_ScholarDepartment_FirstSeen DEFAULT (SYSUTCDATETIME()),
        LastSeenAtUtc DATETIME2(3) NOT NULL,
        LastSyncRunId BIGINT NULL,
        IsCurrent BIT NOT NULL
            CONSTRAINT DF_academiconline_ScholarDepartment_IsCurrent DEFAULT (0),
        CurrentSnapshotRunId BIGINT NULL,
        CONSTRAINT PK_academiconline_ScholarDepartment
            PRIMARY KEY (AcademicOnlineAuthorId, DepartmentId),
        CONSTRAINT FK_academiconline_ScholarDepartment_Scholar
            FOREIGN KEY (AcademicOnlineAuthorId)
            REFERENCES academiconline.Scholar(AcademicOnlineAuthorId),
        CONSTRAINT FK_academiconline_ScholarDepartment_Department
            FOREIGN KEY (DepartmentId)
            REFERENCES academiconline.Department(DepartmentId)
    );

    CREATE INDEX IX_academiconline_ScholarDepartment_Department
        ON academiconline.ScholarDepartment(DepartmentId, AcademicOnlineAuthorId);
END;

IF OBJECT_ID(N'academiconline.Publication', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.Publication
    (
        PublicationKey BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_academiconline_Publication PRIMARY KEY,
        AcademicOnlinePublicationId NVARCHAR(128) NULL,
        OpenAlexId NVARCHAR(128) NULL,
        Doi NVARCHAR(512) NULL,
        DoiNormalized NVARCHAR(512) NULL,
        Title NVARCHAR(MAX) NULL,
        TitleYearFingerprint NVARCHAR(900) NULL,
        Venue NVARCHAR(512) NULL,
        YearText NVARCHAR(50) NULL,
        PublicationYear INT NULL,
        CitationCount INT NULL,
        Verified BIT NULL,
        Link NVARCHAR(1024) NULL,
        CoauthorsJson NVARCHAR(MAX) NULL,
        KeywordsJson NVARCHAR(MAX) NULL,
        RawJson NVARCHAR(MAX) NULL,
        FirstSeenAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_Publication_FirstSeen DEFAULT (SYSUTCDATETIME()),
        LastRetrievedAtUtc DATETIME2(3) NOT NULL,
        LastSyncRunId BIGINT NULL
    );

    CREATE INDEX IX_academiconline_Publication_AcademicOnlineId
        ON academiconline.Publication(AcademicOnlinePublicationId)
        WHERE AcademicOnlinePublicationId IS NOT NULL;

    CREATE INDEX IX_academiconline_Publication_OpenAlexId
        ON academiconline.Publication(OpenAlexId)
        WHERE OpenAlexId IS NOT NULL;

    CREATE INDEX IX_academiconline_Publication_DoiNormalized
        ON academiconline.Publication(DoiNormalized)
        WHERE DoiNormalized IS NOT NULL;

    CREATE INDEX IX_academiconline_Publication_Year
        ON academiconline.Publication(PublicationYear DESC);
END;

IF OBJECT_ID(N'academiconline.ScholarPublication', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.ScholarPublication
    (
        AcademicOnlineAuthorId NVARCHAR(128) NOT NULL,
        PublicationKey BIGINT NOT NULL,
        FirstSeenAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_ScholarPublication_FirstSeen DEFAULT (SYSUTCDATETIME()),
        LastSeenAtUtc DATETIME2(3) NOT NULL,
        LastSyncRunId BIGINT NULL,
        CONSTRAINT PK_academiconline_ScholarPublication
            PRIMARY KEY (AcademicOnlineAuthorId, PublicationKey),
        CONSTRAINT FK_academiconline_ScholarPublication_Scholar
            FOREIGN KEY (AcademicOnlineAuthorId)
            REFERENCES academiconline.Scholar(AcademicOnlineAuthorId),
        CONSTRAINT FK_academiconline_ScholarPublication_Publication
            FOREIGN KEY (PublicationKey)
            REFERENCES academiconline.Publication(PublicationKey)
    );

    CREATE INDEX IX_academiconline_ScholarPublication_Publication
        ON academiconline.ScholarPublication(PublicationKey, AcademicOnlineAuthorId);
END;

IF OBJECT_ID(N'academiconline.DepartmentPublication', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.DepartmentPublication
    (
        DepartmentId BIGINT NOT NULL,
        PublicationKey BIGINT NOT NULL,
        IsCurrent BIT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentPublication_IsCurrent DEFAULT (0),
        FirstSeenAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentPublication_FirstSeen DEFAULT (SYSUTCDATETIME()),
        LastSeenAtUtc DATETIME2(3) NOT NULL,
        LastSyncRunId BIGINT NULL,
        CurrentSnapshotRunId BIGINT NULL,
        CONSTRAINT PK_academiconline_DepartmentPublication
            PRIMARY KEY (DepartmentId, PublicationKey),
        CONSTRAINT FK_academiconline_DepartmentPublication_Department
            FOREIGN KEY (DepartmentId)
            REFERENCES academiconline.Department(DepartmentId),
        CONSTRAINT FK_academiconline_DepartmentPublication_Publication
            FOREIGN KEY (PublicationKey)
            REFERENCES academiconline.Publication(PublicationKey)
    );

    CREATE INDEX IX_academiconline_DepartmentPublication_Current
        ON academiconline.DepartmentPublication
        (
            DepartmentId,
            IsCurrent,
            PublicationKey
        );
END;

IF OBJECT_ID(N'academiconline.vw_DepartmentNewestPublication', N'V') IS NOT NULL
BEGIN
    DROP VIEW academiconline.vw_DepartmentNewestPublication;
END;

EXEC(N'
CREATE VIEW academiconline.vw_DepartmentNewestPublication
AS
SELECT
    d.DepartmentId,
    d.CanonicalName AS DepartmentName,
    p.PublicationKey,
    p.AcademicOnlinePublicationId,
    p.OpenAlexId,
    p.Doi,
    p.Title,
    p.Venue,
    p.YearText,
    p.PublicationYear,
    p.CitationCount,
    p.Verified,
    p.Link,
    p.LastRetrievedAtUtc,
    FacultyCount =
    (
        SELECT COUNT(DISTINCT sp2.AcademicOnlineAuthorId)
        FROM academiconline.ScholarPublication sp2
        JOIN academiconline.ScholarDepartment sd2
            ON sd2.AcademicOnlineAuthorId = sp2.AcademicOnlineAuthorId
           AND sd2.DepartmentId = d.DepartmentId
           AND sd2.IsCurrent = 1
           AND sd2.CurrentSnapshotRunId = d.CurrentSnapshotRunId
        WHERE sp2.PublicationKey = p.PublicationKey
          AND sp2.LastSyncRunId = d.CurrentSnapshotRunId
    ),
    FacultyNames =
        STUFF
        (
            (
                SELECT N''; '' + s2.DisplayName
                FROM academiconline.ScholarPublication sp3
                JOIN academiconline.ScholarDepartment sd3
                    ON sd3.AcademicOnlineAuthorId = sp3.AcademicOnlineAuthorId
                   AND sd3.DepartmentId = d.DepartmentId
                   AND sd3.IsCurrent = 1
                   AND sd3.CurrentSnapshotRunId = d.CurrentSnapshotRunId
                JOIN academiconline.Scholar s2
                    ON s2.AcademicOnlineAuthorId = sp3.AcademicOnlineAuthorId
                WHERE sp3.PublicationKey = p.PublicationKey
                  AND sp3.LastSyncRunId = d.CurrentSnapshotRunId
                GROUP BY s2.DisplayName
                ORDER BY s2.DisplayName
                FOR XML PATH(N''''), TYPE
            ).value(N''.'', N''NVARCHAR(MAX)''),
            1,
            2,
            N''''
        )
FROM academiconline.Department d
JOIN academiconline.DepartmentPublication dp
    ON dp.DepartmentId = d.DepartmentId
   AND dp.IsCurrent = 1
   AND dp.CurrentSnapshotRunId = d.CurrentSnapshotRunId
JOIN academiconline.Publication p
    ON p.PublicationKey = dp.PublicationKey
WHERE d.IsActive = 1
  AND d.CurrentSnapshotRunId IS NOT NULL;
');

PRINT N'PATCH019 Academic Online local reporting store is ready.';
