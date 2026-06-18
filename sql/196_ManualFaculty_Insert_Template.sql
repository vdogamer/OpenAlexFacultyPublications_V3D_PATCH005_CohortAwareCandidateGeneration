/*
Manual Faculty Insert Template — V3D PATCH002

Use this only if the web /Admin/AddFaculty page is not available.
Replace sample rows with actual faculty from the website/internal source.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.Faculty', N'U') IS NULL
BEGIN
    RAISERROR('openalex.Faculty does not exist.', 16, 1);
    RETURN;
END;

PRINT 'Faculty columns';

SELECT
    c.column_id,
    c.name AS ColumnName,
    t.name AS DataType,
    c.max_length,
    c.is_nullable
FROM sys.columns c
JOIN sys.types t
    ON c.user_type_id = t.user_type_id
WHERE c.object_id = OBJECT_ID(N'openalex.Faculty')
ORDER BY c.column_id;

PRINT 'Current faculty count';

SELECT COUNT(*) AS TotalFaculty
FROM openalex.Faculty;

/*
INSERT INTO openalex.Faculty
(
    DisplayName,
    PrimaryEmail,
    DepartmentName,
    DivisionName,
    AppointmentTitle,
    Orcid,
    ClaimedOpenAlexAuthorId,
    IsActive,
    CreatedAtUtc,
    UpdatedAtUtc
)
VALUES
(
    N'Actual Faculty Name',
    N'faculty.email@virginia.edu',
    N'Department Name',
    N'Division Name',
    N'Appointment Title',
    N'0000-0000-0000-0000',
    N'A1234567890',
    1,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);
*/

PRINT 'Uncomment and edit the INSERT block for each actual faculty member you need to add.';
