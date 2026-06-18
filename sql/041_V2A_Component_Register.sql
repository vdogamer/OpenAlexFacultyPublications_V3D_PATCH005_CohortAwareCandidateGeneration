/*
OpenAlex Version 2A — Component Register
Creates a lightweight metadata table to record integrated components.
*/

SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'openalex')
BEGIN
    RAISERROR('The openalex schema does not exist. Run Version 1A first.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'openalex.ApplicationComponent', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.ApplicationComponent
    (
        ApplicationComponentId BIGINT IDENTITY(1,1) NOT NULL,
        ComponentKey           VARCHAR(100) NOT NULL,
        DisplayName            NVARCHAR(256) NOT NULL,
        ComponentType          VARCHAR(100) NOT NULL,
        Description            NVARCHAR(1000) NULL,
        IsActive               BIT NOT NULL CONSTRAINT DF_ApplicationComponent_IsActive DEFAULT 1,
        CreatedAtUtc           DATETIME2(7) NOT NULL CONSTRAINT DF_ApplicationComponent_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc           DATETIME2(7) NOT NULL CONSTRAINT DF_ApplicationComponent_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_ApplicationComponent PRIMARY KEY CLUSTERED (ApplicationComponentId),
        CONSTRAINT UQ_ApplicationComponent_ComponentKey UNIQUE (ComponentKey)
    );
END
GO

MERGE openalex.ApplicationComponent AS target
USING
(
    SELECT 'OpenAlex.Web' AS ComponentKey, N'OpenAlex Web App' AS DisplayName, 'WebApp' AS ComponentType, N'Integrated Razor Pages app for review, reporting, jobs, auth, and operations.' AS Description
    UNION ALL SELECT 'OpenAlex.Worker', N'OpenAlex Worker Host', 'Worker', N'Integrated background worker host for scheduled ingestion and exports.'
    UNION ALL SELECT 'OpenAlex.Data', N'OpenAlex Data Library', 'Library', N'Shared SQL Server connection and data helpers.'
    UNION ALL SELECT 'OpenAlex.OpenAlexClient', N'OpenAlex API Client Library', 'Library', N'Shared OpenAlex API HTTP and cursor paging client.'
    UNION ALL SELECT 'OpenAlex.Matching', N'OpenAlex Matching Library', 'Library', N'Shared matching/scoring/evidence rules.'
    UNION ALL SELECT 'OpenAlex.Reporting', N'OpenAlex Reporting Library', 'Library', N'Shared CSV/reporting services.'
    UNION ALL SELECT 'OpenAlex.OperationalLogging', N'OpenAlex Operational Logging Library', 'Library', N'Shared operational logging and error capture services.'
) AS source
ON target.ComponentKey = source.ComponentKey
WHEN MATCHED THEN
    UPDATE SET
        DisplayName = source.DisplayName,
        ComponentType = source.ComponentType,
        Description = source.Description,
        IsActive = 1,
        UpdatedAtUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (ComponentKey, DisplayName, ComponentType, Description, IsActive, CreatedAtUtc, UpdatedAtUtc)
    VALUES (source.ComponentKey, source.DisplayName, source.ComponentType, source.Description, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
GO

SELECT
    ComponentKey,
    DisplayName,
    ComponentType,
    Description,
    IsActive,
    UpdatedAtUtc
FROM openalex.ApplicationComponent
ORDER BY ComponentType, ComponentKey;
GO
