/*
V3E PATCH019B — Department Current-State Reconciliation
SQL Server 2016 compatible.

Purpose:
- Preserve a known-good current department snapshot.
- Do not let partial syncs replace complete reporting state.
- Reconcile current scholar membership and current department publication membership.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'academiconline.Department', N'U') IS NULL
   OR OBJECT_ID(N'academiconline.ScholarDepartment', N'U') IS NULL
   OR OBJECT_ID(N'academiconline.ScholarPublication', N'U') IS NULL
   OR OBJECT_ID(N'academiconline.SyncRun', N'U') IS NULL
BEGIN
    RAISERROR(
        N'PATCH019/PATCH019A objects are missing. Apply sql/411 and sql/413 before PATCH019B.',
        16,
        1);
    RETURN;
END;

IF COL_LENGTH(N'academiconline.Department', N'CurrentSnapshotRunId') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD CurrentSnapshotRunId BIGINT NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'CurrentSnapshotAtUtc') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD CurrentSnapshotAtUtc DATETIME2(3) NULL;
END;

IF COL_LENGTH(N'academiconline.ScholarDepartment', N'IsCurrent') IS NULL
BEGIN
    ALTER TABLE academiconline.ScholarDepartment
        ADD IsCurrent BIT NOT NULL
            CONSTRAINT DF_academiconline_ScholarDepartment_IsCurrent
            DEFAULT (0);
END;

IF COL_LENGTH(N'academiconline.ScholarDepartment', N'CurrentSnapshotRunId') IS NULL
BEGIN
    ALTER TABLE academiconline.ScholarDepartment
        ADD CurrentSnapshotRunId BIGINT NULL;
END;

IF COL_LENGTH(N'academiconline.SyncRun', N'SnapshotPromoted') IS NULL
BEGIN
    ALTER TABLE academiconline.SyncRun
        ADD SnapshotPromoted BIT NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_SnapshotPromoted
            DEFAULT (0);
END;

IF OBJECT_ID(N'academiconline.DepartmentPublication', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.DepartmentPublication
    (
        DepartmentId BIGINT NOT NULL,
        PublicationKey BIGINT NOT NULL,
        IsCurrent BIT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentPublication_IsCurrent
            DEFAULT (0),
        FirstSeenAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentPublication_FirstSeen
            DEFAULT (SYSUTCDATETIME()),
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

/*
Backfill the latest known complete PATCH019A run for each department.
This preserves the already validated Cell Biology run without requiring
another API sync merely to initialize snapshot state.
*/
;WITH RankedCompleteRuns AS
(
    SELECT
        r.AcademicOnlineSyncRunId,
        r.DepartmentName,
        r.CompletedAtUtc,
        rn = ROW_NUMBER() OVER
        (
            PARTITION BY r.DepartmentName
            ORDER BY
                r.CompletedAtUtc DESC,
                r.AcademicOnlineSyncRunId DESC
        )
    FROM academiconline.SyncRun r
    WHERE r.RunStatus = N'Completed'
      AND r.IsCacheComplete = 1
)
UPDATE d
SET
    d.CurrentSnapshotRunId = r.AcademicOnlineSyncRunId,
    d.CurrentSnapshotAtUtc = r.CompletedAtUtc
FROM academiconline.Department d
JOIN RankedCompleteRuns r
    ON r.DepartmentName = d.SourceName
   AND r.rn = 1
WHERE d.CurrentSnapshotRunId IS NULL;

UPDATE sd
SET
    sd.IsCurrent =
        CASE
            WHEN sd.LastSyncRunId = d.CurrentSnapshotRunId THEN 1
            ELSE 0
        END,
    sd.CurrentSnapshotRunId = d.CurrentSnapshotRunId
FROM academiconline.ScholarDepartment sd
JOIN academiconline.Department d
    ON d.DepartmentId = sd.DepartmentId
WHERE d.CurrentSnapshotRunId IS NOT NULL;

INSERT INTO academiconline.DepartmentPublication
(
    DepartmentId,
    PublicationKey,
    IsCurrent,
    FirstSeenAtUtc,
    LastSeenAtUtc,
    LastSyncRunId,
    CurrentSnapshotRunId
)
SELECT DISTINCT
    d.DepartmentId,
    sp.PublicationKey,
    1,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    d.CurrentSnapshotRunId,
    d.CurrentSnapshotRunId
FROM academiconline.Department d
JOIN academiconline.ScholarDepartment sd
    ON sd.DepartmentId = d.DepartmentId
   AND sd.IsCurrent = 1
JOIN academiconline.ScholarPublication sp
    ON sp.AcademicOnlineAuthorId = sd.AcademicOnlineAuthorId
   AND sp.LastSyncRunId = d.CurrentSnapshotRunId
WHERE d.CurrentSnapshotRunId IS NOT NULL
  AND NOT EXISTS
  (
      SELECT 1
      FROM academiconline.DepartmentPublication dp
      WHERE dp.DepartmentId = d.DepartmentId
        AND dp.PublicationKey = sp.PublicationKey
  );

UPDATE r
SET r.SnapshotPromoted = 1
FROM academiconline.SyncRun r
JOIN academiconline.Department d
    ON d.CurrentSnapshotRunId = r.AcademicOnlineSyncRunId;

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

PRINT N'PATCH019B current-state reconciliation is ready.';
