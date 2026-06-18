/* OpenAlex Version 3B — Persisted Scheduler Tables */
SET NOCOUNT ON;

IF SCHEMA_ID(N'openalex') IS NULL
BEGIN
    EXEC(N'CREATE SCHEMA openalex');
END
GO

IF OBJECT_ID(N'openalex.JobDefinition', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.JobDefinition
    (
        JobDefinitionId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobDefinition PRIMARY KEY,
        JobName NVARCHAR(200) NOT NULL,
        JobDisplayName NVARCHAR(300) NOT NULL,
        JobCategory NVARCHAR(100) NOT NULL,
        JobDescription NVARCHAR(MAX) NULL,
        IsEnabled BIT NOT NULL CONSTRAINT DF_JobDefinition_IsEnabled DEFAULT (1),
        IsWriteJob BIT NOT NULL CONSTRAINT DF_JobDefinition_IsWriteJob DEFAULT (1),
        RequiresConfirmation BIT NOT NULL CONSTRAINT DF_JobDefinition_RequiresConfirmation DEFAULT (1),
        DefaultRunMode NVARCHAR(50) NOT NULL CONSTRAINT DF_JobDefinition_DefaultRunMode DEFAULT ('TestLoad'),
        DefaultMaxPages INT NULL,
        DefaultMaxRecords INT NULL,
        SortOrder INT NOT NULL CONSTRAINT DF_JobDefinition_SortOrder DEFAULT (100),
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_JobDefinition_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_JobDefinition_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_JobDefinition_JobName UNIQUE (JobName)
    );
END
GO

IF OBJECT_ID(N'openalex.JobExecutionRequest', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.JobExecutionRequest
    (
        JobExecutionRequestId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobExecutionRequest PRIMARY KEY,
        JobDefinitionId BIGINT NOT NULL,
        JobName NVARCHAR(200) NOT NULL,
        RequestStatus NVARCHAR(50) NOT NULL CONSTRAINT DF_JobExecutionRequest_RequestStatus DEFAULT ('Queued'),
        RequestedBy NVARCHAR(256) NOT NULL,
        RequestedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_JobExecutionRequest_RequestedAtUtc DEFAULT SYSUTCDATETIME(),
        RequestReason NVARCHAR(MAX) NULL,
        ConfirmationText NVARCHAR(200) NULL,
        RunMode NVARCHAR(50) NOT NULL CONSTRAINT DF_JobExecutionRequest_RunMode DEFAULT ('TestLoad'),
        MaxPages INT NULL,
        MaxRecords INT NULL,
        StartedAtUtc DATETIME2(7) NULL,
        FinishedAtUtc DATETIME2(7) NULL,
        WorkerMachineName NVARCHAR(256) NULL,
        WorkerApplicationVersion NVARCHAR(50) NULL,
        IngestionRunId BIGINT NULL,
        ErrorMessage NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_JobExecutionRequest_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_JobExecutionRequest_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_JobExecutionRequest_JobDefinition FOREIGN KEY (JobDefinitionId) REFERENCES openalex.JobDefinition(JobDefinitionId),
        CONSTRAINT CK_JobExecutionRequest_RequestStatus CHECK (RequestStatus IN ('Queued','Started','Completed','CompletedWithWarnings','Failed','Cancelled','Skipped')),
        CONSTRAINT CK_JobExecutionRequest_RunMode CHECK (RunMode IN ('SmokeTest','TestLoad','FullLoad'))
    );
END
GO

IF OBJECT_ID(N'openalex.JobExecutionEvent', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.JobExecutionEvent
    (
        JobExecutionEventId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobExecutionEvent PRIMARY KEY,
        JobExecutionRequestId BIGINT NOT NULL,
        EventName NVARCHAR(200) NOT NULL,
        EventLevel NVARCHAR(50) NOT NULL CONSTRAINT DF_JobExecutionEvent_EventLevel DEFAULT ('Information'),
        EventMessage NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_JobExecutionEvent_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_JobExecutionEvent_JobExecutionRequest FOREIGN KEY (JobExecutionRequestId) REFERENCES openalex.JobExecutionRequest(JobExecutionRequestId),
        CONSTRAINT CK_JobExecutionEvent_EventLevel CHECK (EventLevel IN ('Trace','Debug','Information','Warning','Error','Critical'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_JobExecutionRequest_Status_RequestedAtUtc' AND object_id=OBJECT_ID(N'openalex.JobExecutionRequest'))
BEGIN
    CREATE INDEX IX_JobExecutionRequest_Status_RequestedAtUtc ON openalex.JobExecutionRequest(RequestStatus, RequestedAtUtc);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_JobExecutionEvent_Request_CreatedAtUtc' AND object_id=OBJECT_ID(N'openalex.JobExecutionEvent'))
BEGIN
    CREATE INDEX IX_JobExecutionEvent_Request_CreatedAtUtc ON openalex.JobExecutionEvent(JobExecutionRequestId, CreatedAtUtc);
END
GO
