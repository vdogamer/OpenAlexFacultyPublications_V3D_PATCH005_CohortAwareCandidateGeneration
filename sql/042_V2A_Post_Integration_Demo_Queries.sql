/*
OpenAlex Version 2A — Post Integration Demo Queries
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

IF OBJECT_ID(N'openalex.ApplicationComponent', N'U') IS NOT NULL
BEGIN
    SELECT
        ComponentKey,
        DisplayName,
        ComponentType,
        IsActive,
        UpdatedAtUtc
    FROM openalex.ApplicationComponent
    ORDER BY ComponentType, ComponentKey;
END

IF OBJECT_ID(N'openalex.OperationalLog', N'U') IS NOT NULL
BEGIN
    SELECT TOP 25
        OperationalLogId,
        SourceApplication,
        EnvironmentName,
        LogLevel,
        Category,
        EventName,
        Message,
        CreatedAtUtc
    FROM openalex.OperationalLog
    WHERE SourceApplication IN ('OpenAlex.Web', 'OpenAlex.Worker', 'OpenAlexFacultyPublications')
    ORDER BY OperationalLogId DESC;
END

IF OBJECT_ID(N'openalex.vw_FacultyPublicationExport', N'V') IS NOT NULL
BEGIN
    SELECT TOP 20
        FacultyDisplayName,
        PrimaryEmail,
        DepartmentName,
        WorkTitle,
        PublicationYear,
        WorkDoi,
        DoiUrl,
        WorkOpenAlexId
    FROM openalex.vw_FacultyPublicationExport
    ORDER BY FacultyDisplayName, PublicationYear DESC, WorkTitle;
END
