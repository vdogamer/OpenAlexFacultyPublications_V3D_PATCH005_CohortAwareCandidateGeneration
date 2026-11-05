/* Session-local input contract for 454. No application-table writes. */
SET NOCOUNT ON;
IF OBJECT_ID(N'tempdb..#SomRosterInput') IS NOT NULL DROP TABLE #SomRosterInput;
IF OBJECT_ID(N'tempdb..#SomRosterContext') IS NOT NULL DROP TABLE #SomRosterContext;
CREATE TABLE #SomRosterContext
(
    PreviewOnly bit NOT NULL,
    ExpectedCandidates int NOT NULL,
    ExpectedAccepted int NOT NULL,
    ExpectedQuarantined int NOT NULL,
    SourceServer nvarchar(128) NOT NULL,
    SourceDatabase nvarchar(128) NOT NULL,
    SourceExportRunId uniqueidentifier NOT NULL,
    SourceCapturedAtUtc datetime2(7) NOT NULL,
    IdentityEvidenceExportRunId uniqueidentifier NULL,
    ExpectedPayloadHash varbinary(32) NOT NULL
);
CREATE TABLE #SomRosterInput
(
    SourceUserName nvarchar(255) NOT NULL,
    ComputingId nvarchar(32) NULL,
    DisplayName nvarchar(512) NOT NULL,
    FirstName nvarchar(255) NULL,
    LastName nvarchar(255) NULL,
    PrimarySchoolCode nvarchar(20) NOT NULL,
    FacultyClassification nvarchar(50) NOT NULL,
    DepartmentCode nvarchar(100) NULL,
    OrganizationName nvarchar(512) NULL,
    AcademicRank nvarchar(255) NULL,
    AppointmentTitle nvarchar(512) NULL,
    RawDirectoryEmail nvarchar(512) NULL,
    DirectoryEmail nvarchar(320) NULL,
    DirectoryPersonId int NULL,
    DirectoryAccountStatus nvarchar(50) NULL,
    BaselineIdentityStatus nvarchar(40) NOT NULL,
    Disposition nvarchar(16) NOT NULL,
    ValidationNote nvarchar(512) NULL,
    SourceRecordHash varbinary(32) NOT NULL
);
