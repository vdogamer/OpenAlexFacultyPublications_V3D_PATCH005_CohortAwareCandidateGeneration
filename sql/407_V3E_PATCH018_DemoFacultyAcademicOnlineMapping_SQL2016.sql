/*
V3E PATCH018 - Manager Demo Mapping Seed
Creates/refreshes a durable current Academic Online mapping for one faculty member
using the latest previously captured AcademicOnlineScholarSnapshot.

Default demo faculty: FacultyId 16 (Charles R. Farber).
Change @FacultyId only if you intentionally want a different demo profile.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @FacultyId BIGINT = 16;
DECLARE @VerifiedBy NVARCHAR(512) = N'OpenAlex.V3E.PATCH018.Demo';

IF OBJECT_ID(N'openalex.FacultyAcademicOnlineAuthorMap', N'U') IS NULL
BEGIN
    RAISERROR('Run 406_V3E_PATCH018_FacultyAcademicOnlineAuthorMap_SQL2016.sql first.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'openalex.AcademicOnlineScholarSnapshot', N'U') IS NULL
BEGIN
    RAISERROR('Academic Online scholar snapshots do not exist.', 16, 1);
    RETURN;
END;

DECLARE
    @AcademicOnlineAuthorId NVARCHAR(128),
    @AcademicOnlineName NVARCHAR(512),
    @AcademicOnlineEmail NVARCHAR(512),
    @AcademicOnlineOrcidUrl NVARCHAR(512);

SELECT TOP (1)
    @AcademicOnlineAuthorId = s.AcademicOnlineAuthorId,
    @AcademicOnlineName = s.AcademicOnlineName,
    @AcademicOnlineEmail = s.AcademicOnlineEmail,
    @AcademicOnlineOrcidUrl = s.AcademicOnlineOrcidUrl
FROM openalex.AcademicOnlineScholarSnapshot s
WHERE s.FacultyId = @FacultyId
  AND NULLIF(LTRIM(RTRIM(s.AcademicOnlineAuthorId)), '') IS NOT NULL
ORDER BY
    s.AcademicOnlineScholarSnapshotId DESC;

IF @AcademicOnlineAuthorId IS NULL
BEGIN
    RAISERROR('No prior Academic Online scholar snapshot with an author ID was found for the selected FacultyId.', 16, 1);
    RETURN;
END;

BEGIN TRANSACTION;

UPDATE openalex.FacultyAcademicOnlineAuthorMap
SET
    IsCurrent = 0,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyId = @FacultyId
  AND IsCurrent = 1
  AND AcademicOnlineAuthorId <> @AcademicOnlineAuthorId;

IF EXISTS
(
    SELECT 1
    FROM openalex.FacultyAcademicOnlineAuthorMap
    WHERE FacultyId = @FacultyId
      AND AcademicOnlineAuthorId = @AcademicOnlineAuthorId
)
BEGIN
    UPDATE openalex.FacultyAcademicOnlineAuthorMap
    SET
        AcademicOnlineName = @AcademicOnlineName,
        AcademicOnlineEmail = @AcademicOnlineEmail,
        AcademicOnlineOrcidUrl = @AcademicOnlineOrcidUrl,
        MappingSource = 'PATCH018-LatestAcademicOnlineSnapshot',
        EvidenceNotes = N'Demo mapping promoted from latest previously captured Academic Online scholar snapshot.',
        IsCurrent = 1,
        VerifiedBy = @VerifiedBy,
        VerifiedAtUtc = SYSUTCDATETIME(),
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE FacultyId = @FacultyId
      AND AcademicOnlineAuthorId = @AcademicOnlineAuthorId;
END
ELSE
BEGIN
    INSERT INTO openalex.FacultyAcademicOnlineAuthorMap
    (
        FacultyId,
        AcademicOnlineAuthorId,
        AcademicOnlineName,
        AcademicOnlineEmail,
        AcademicOnlineOrcidUrl,
        MappingSource,
        EvidenceNotes,
        IsCurrent,
        VerifiedBy,
        VerifiedAtUtc,
        CreatedAtUtc,
        UpdatedAtUtc
    )
    VALUES
    (
        @FacultyId,
        @AcademicOnlineAuthorId,
        @AcademicOnlineName,
        @AcademicOnlineEmail,
        @AcademicOnlineOrcidUrl,
        'PATCH018-LatestAcademicOnlineSnapshot',
        N'Demo mapping promoted from latest previously captured Academic Online scholar snapshot.',
        1,
        @VerifiedBy,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );
END;

COMMIT TRANSACTION;

SELECT
    m.FacultyAcademicOnlineAuthorMapId,
    m.FacultyId,
    f.DisplayName,
    f.DepartmentName,
    m.AcademicOnlineAuthorId,
    m.AcademicOnlineName,
    m.AcademicOnlineEmail,
    m.AcademicOnlineOrcidUrl,
    m.MappingSource,
    m.IsCurrent,
    m.VerifiedBy,
    m.VerifiedAtUtc,
    MappingStatus = 'ReadyForAcademicOnlineHtmlDemo'
FROM openalex.FacultyAcademicOnlineAuthorMap m
INNER JOIN openalex.Faculty f
    ON f.FacultyId = m.FacultyId
WHERE m.FacultyId = @FacultyId
  AND m.IsCurrent = 1;
