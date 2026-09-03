/*
V3E PATCH020 — Academic Online Department Catalog + Controlled Scheduled Refresh
SQL Server 2016 compatible.

Run after PATCH019B.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'academiconline.Department', N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.Department does not exist. Apply PATCH019 through PATCH019B first.',
        16,
        1);
    RETURN;
END;

IF COL_LENGTH(N'academiconline.Department', N'RefreshEnabled') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD RefreshEnabled BIT NOT NULL
            CONSTRAINT DF_academiconline_Department_RefreshEnabled
            DEFAULT (0);
END;

IF COL_LENGTH(N'academiconline.Department', N'RefreshIntervalHours') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD RefreshIntervalHours INT NOT NULL
            CONSTRAINT DF_academiconline_Department_RefreshIntervalHours
            DEFAULT (168);
END;

IF COL_LENGTH(N'academiconline.Department', N'RefreshFailureRetryMinutes') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD RefreshFailureRetryMinutes INT NOT NULL
            CONSTRAINT DF_academiconline_Department_RefreshFailureRetryMinutes
            DEFAULT (60);
END;

IF COL_LENGTH(N'academiconline.Department', N'RefreshFromYear') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD RefreshFromYear INT NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'RefreshToYear') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD RefreshToYear INT NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'RefreshVerifiedOnly') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD RefreshVerifiedOnly BIT NOT NULL
            CONSTRAINT DF_academiconline_Department_RefreshVerifiedOnly
            DEFAULT (0);
END;

IF COL_LENGTH(N'academiconline.Department', N'RefreshMaxScholars') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD RefreshMaxScholars INT NOT NULL
            CONSTRAINT DF_academiconline_Department_RefreshMaxScholars
            DEFAULT (250);
END;

IF COL_LENGTH(N'academiconline.Department', N'NextRefreshDueAtUtc') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD NextRefreshDueAtUtc DATETIME2(3) NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'LastRefreshAttemptAtUtc') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD LastRefreshAttemptAtUtc DATETIME2(3) NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'LastRefreshAttemptStatus') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD LastRefreshAttemptStatus NVARCHAR(30) NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'LastRefreshAttemptRunId') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD LastRefreshAttemptRunId BIGINT NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'LastRefreshError') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD LastRefreshError NVARCHAR(MAX) NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'RefreshLeaseOwner') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD RefreshLeaseOwner NVARCHAR(256) NULL;
END;

IF COL_LENGTH(N'academiconline.Department', N'RefreshLeaseUntilUtc') IS NULL
BEGIN
    ALTER TABLE academiconline.Department
        ADD RefreshLeaseUntilUtc DATETIME2(3) NULL;
END;

GO

/* Seed policy from the currently promoted snapshot scope where possible.
   Existing departments remain disabled until an administrator explicitly enables them. */
UPDATE d
SET
    RefreshFromYear =
        COALESCE(d.RefreshFromYear, r.FromYear),
    RefreshToYear =
        COALESCE(d.RefreshToYear, r.ToYear),
    RefreshVerifiedOnly =
        CASE
            WHEN r.AcademicOnlineSyncRunId IS NULL
                THEN d.RefreshVerifiedOnly
            ELSE r.VerifiedOnly
        END,
    NextRefreshDueAtUtc =
        COALESCE
        (
            d.NextRefreshDueAtUtc,
            DATEADD
            (
                HOUR,
                d.RefreshIntervalHours,
                COALESCE(d.CurrentSnapshotAtUtc, SYSUTCDATETIME())
            )
        )
FROM academiconline.Department d
LEFT JOIN academiconline.SyncRun r
    ON r.AcademicOnlineSyncRunId = d.CurrentSnapshotRunId;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'academiconline.Department')
      AND name = N'IX_academiconline_Department_RefreshDue'
)
BEGIN
    CREATE INDEX IX_academiconline_Department_RefreshDue
        ON academiconline.Department
        (
            RefreshEnabled,
            NextRefreshDueAtUtc,
            RefreshLeaseUntilUtc,
            DepartmentId
        );
END;

PRINT N'PATCH020 department catalog and controlled refresh policy are ready.';
