/*
V3E PATCH020A — Continuous Scheduler Heartbeat + Operational Status
SQL Server 2016 compatible.

Run after PATCH020.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SCHEMA_ID(N'academiconline') IS NULL
BEGIN
    RAISERROR(
        N'academiconline schema does not exist. Apply PATCH019 through PATCH020 first.',
        16,
        1);
    RETURN;
END;

IF OBJECT_ID(N'academiconline.RefreshSchedulerState', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.RefreshSchedulerState
    (
        SchedulerKey NVARCHAR(100) NOT NULL
            CONSTRAINT PK_academiconline_RefreshSchedulerState PRIMARY KEY,
        LastHeartbeatAtUtc DATETIME2(3) NOT NULL,
        LastPassStartedAtUtc DATETIME2(3) NULL,
        LastPassCompletedAtUtc DATETIME2(3) NULL,
        LastPassStatus NVARCHAR(30) NULL,
        LeaseOwner NVARCHAR(256) NULL,
        PollIntervalSeconds INT NULL,
        DepartmentsClaimed INT NOT NULL
            CONSTRAINT DF_academiconline_RefreshSchedulerState_Claimed DEFAULT (0),
        DepartmentsCompleted INT NOT NULL
            CONSTRAINT DF_academiconline_RefreshSchedulerState_Completed DEFAULT (0),
        DepartmentsPartial INT NOT NULL
            CONSTRAINT DF_academiconline_RefreshSchedulerState_Partial DEFAULT (0),
        DepartmentsFailed INT NOT NULL
            CONSTRAINT DF_academiconline_RefreshSchedulerState_Failed DEFAULT (0),
        LastError NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_RefreshSchedulerState_Created DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_RefreshSchedulerState_Updated DEFAULT (SYSUTCDATETIME())
    );
END;

PRINT N'PATCH020A scheduler heartbeat state is ready.';
