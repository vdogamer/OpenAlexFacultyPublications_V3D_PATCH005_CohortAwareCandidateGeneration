USE [OpenAlexIngestion_Test];
GO
SET NOCOUNT ON;
IF OBJECT_ID(N'openalex.FacultyRoster',N'U') IS NULL
    THROW 51430,'No roster has been loaded. Run the load script first.',1;
DECLARE @VerifyRun uniqueidentifier;
SELECT TOP(1) @VerifyRun=ImportRunId FROM openalex.FacultyRosterImportRun ORDER BY ImportedAtUtc DESC,ImportRunId;
SELECT r.ImportRunId,r.ImportedAtUtc,r.SourceRecords,r.AcceptedFaculty,r.QuarantinedRecords,
       r.InsertedRows,r.ReusedRows,
       (SELECT COUNT(*) FROM openalex.FacultyRosterImportRow m JOIN openalex.FacultyRoster f
         ON f.ComputingId=m.ComputingId AND f.SourceRecordHash=m.SourceRecordHash
         WHERE m.ImportRunId=r.ImportRunId AND m.Disposition=N'Import') AS VerifiedRosterRows,
       r.SourceExportRunId,r.SourceCapturedAtUtc,r.ScopeDescription
FROM openalex.FacultyRosterImportRun r WHERE r.ImportRunId=@VerifyRun;
SELECT ComputingId,DisplayName,Disposition,ValidationNote,SourceUserName
FROM openalex.FacultyRosterImportRow WHERE ImportRunId=@VerifyRun AND Disposition=N'Quarantine';
SELECT f.ComputingId,f.DisplayName,f.DepartmentCode,f.OrganizationName,f.DirectoryEmail,
       f.DirectoryAccountStatus,f.BaselineIdentityStatus,f.NativeFacultyId,f.NativeLinkStatus,
       f.SourceFreshnessVerified,f.ValidationNote
FROM openalex.FacultyRoster f JOIN openalex.FacultyRosterImportRow m ON m.ComputingId=f.ComputingId
WHERE m.ImportRunId=@VerifyRun AND m.Disposition=N'Import'
ORDER BY f.DisplayName,f.ComputingId;
