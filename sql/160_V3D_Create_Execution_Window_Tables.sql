/* OpenAlex Version 3D — Execution Window Tables */
SET NOCOUNT ON;
IF SCHEMA_ID(N'openalex') IS NULL BEGIN EXEC(N'CREATE SCHEMA openalex'); END
GO
IF OBJECT_ID(N'openalex.JobExecutionWindow', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.JobExecutionWindow
    (
        JobExecutionWindowId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobExecutionWindow PRIMARY KEY,
        WindowName NVARCHAR(200) NOT NULL,
        WindowDescription NVARCHAR(MAX) NULL,
        AppliesToJobName NVARCHAR(200) NULL,
        IsEnabled BIT NOT NULL CONSTRAINT DF_JobExecutionWindow_IsEnabled DEFAULT (1),
        DaysOfWeekCsv NVARCHAR(100) NOT NULL CONSTRAINT DF_JobExecutionWindow_Days DEFAULT ('Monday,Tuesday,Wednesday,Thursday,Friday'),
        StartTimeLocal TIME(0) NOT NULL,
        EndTimeLocal TIME(0) NOT NULL,
        TimeZoneId NVARCHAR(100) NOT NULL CONSTRAINT DF_JobExecutionWindow_TimeZone DEFAULT ('Eastern Standard Time'),
        AllowFullLoad BIT NOT NULL CONSTRAINT DF_JobExecutionWindow_AllowFullLoad DEFAULT (0),
        MaxRequestsPerWindow INT NOT NULL CONSTRAINT DF_JobExecutionWindow_MaxRequests DEFAULT (1),
        CreatedBy NVARCHAR(256) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_JobExecutionWindow_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy NVARCHAR(256) NULL,
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_JobExecutionWindow_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_JobExecutionWindow_MaxRequests CHECK (MaxRequestsPerWindow >= 1),
        CONSTRAINT CK_JobExecutionWindow_Times CHECK (StartTimeLocal <> EndTimeLocal)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_JobExecutionWindow_IsEnabled_JobName' AND object_id=OBJECT_ID(N'openalex.JobExecutionWindow'))
BEGIN
    CREATE INDEX IX_JobExecutionWindow_IsEnabled_JobName ON openalex.JobExecutionWindow(IsEnabled, AppliesToJobName);
END
GO
