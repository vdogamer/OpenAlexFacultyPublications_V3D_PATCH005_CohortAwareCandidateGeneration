/*
OpenAlex Version 2B — Demo Queries
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

IF OBJECT_ID(N'openalex.Faculty', N'U') IS NOT NULL
BEGIN
    SELECT TOP 20
        FacultyId,
        DisplayName,
        PrimaryEmail,
        DepartmentName,
        DivisionName,
        AppointmentTitle,
        Orcid,
        ClaimedOpenAlexAuthorId,
        IsActive
    FROM openalex.Faculty
    ORDER BY DisplayName;
END

IF OBJECT_ID(N'openalex.vw_FacultyPublicationReviewQueue', N'V') IS NOT NULL
BEGIN
    SELECT TOP 20
        FacultyDisplayName,
        WorkTitle,
        PublicationYear,
        DoiUrl,
        AuthorDisplayName,
        CandidateStatus,
        MatchScore,
        EvidenceSummary,
        CreatedAtUtc
    FROM openalex.vw_FacultyPublicationReviewQueue
    ORDER BY CreatedAtUtc DESC;
END

IF OBJECT_ID(N'openalex.vw_FacultyPublicationExport', N'V') IS NOT NULL
BEGIN
    SELECT TOP 20
        FacultyDisplayName,
        PrimaryEmail,
        DepartmentName,
        WorkTitle,
        PublicationYear,
        DoiUrl,
        WorkOpenAlexId,
        DecisionStatus,
        ReviewedBy,
        ReviewedAtUtc
    FROM openalex.vw_FacultyPublicationExport
    ORDER BY FacultyDisplayName, PublicationYear DESC;
END

IF OBJECT_ID(N'openalex.OperationalLog', N'U') IS NOT NULL
BEGIN
    SELECT TOP 20
        OperationalLogId,
        SourceApplication,
        EnvironmentName,
        LogLevel,
        Category,
        EventName,
        Message,
        CreatedAtUtc
    FROM openalex.OperationalLog
    ORDER BY OperationalLogId DESC;
END
