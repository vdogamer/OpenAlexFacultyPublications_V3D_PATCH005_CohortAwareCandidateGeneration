/*
V3E PATCH019A — Department Cache Completeness
SQL Server 2016 compatible.

Run after PATCH019 sql/411.

Adds explicit completeness metadata to each Academic Online department sync run.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'academiconline.SyncRun', N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.SyncRun does not exist. Run sql/411_V3E_PATCH019_AcademicOnlineLocalReportingStore_SQL2016.sql first.',
        16,
        1);
    RETURN;
END;

IF COL_LENGTH(N'academiconline.SyncRun', N'ScholarsDiscovered') IS NULL
BEGIN
    ALTER TABLE academiconline.SyncRun
        ADD ScholarsDiscovered INT NULL;
END;

IF COL_LENGTH(N'academiconline.SyncRun', N'MaxScholarsToProcess') IS NULL
BEGIN
    ALTER TABLE academiconline.SyncRun
        ADD MaxScholarsToProcess INT NULL;
END;

IF COL_LENGTH(N'academiconline.SyncRun', N'ScholarPaginationSafeguardHit') IS NULL
BEGIN
    ALTER TABLE academiconline.SyncRun
        ADD ScholarPaginationSafeguardHit BIT NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_ScholarPaginationSafeguardHit
            DEFAULT (0);
END;

IF COL_LENGTH(N'academiconline.SyncRun', N'PublicationPaginationSafeguardHitCount') IS NULL
BEGIN
    ALTER TABLE academiconline.SyncRun
        ADD PublicationPaginationSafeguardHitCount INT NOT NULL
            CONSTRAINT DF_academiconline_SyncRun_PublicationPaginationSafeguardHitCount
            DEFAULT (0);
END;

IF COL_LENGTH(N'academiconline.SyncRun', N'IsCacheComplete') IS NULL
BEGIN
    ALTER TABLE academiconline.SyncRun
        ADD IsCacheComplete BIT NULL;
END;

PRINT N'PATCH019A department cache completeness columns are ready.';
