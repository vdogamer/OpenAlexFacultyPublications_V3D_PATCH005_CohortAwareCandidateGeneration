/*
Manual Faculty Count and 15-Cohort Next Step
*/

SET NOCOUNT ON;

SELECT COUNT(*) AS TotalFaculty
FROM openalex.Faculty;

SELECT TOP 25 *
FROM openalex.Faculty
ORDER BY FacultyId DESC;

IF OBJECT_ID(N'openalex.PilotFacultyCohort', N'U') IS NOT NULL
BEGIN
    SELECT
        CohortName,
        COUNT(*) AS EnabledFacultyCount
    FROM openalex.PilotFacultyCohort
    WHERE IsEnabled = 1
    GROUP BY CohortName;
END
ELSE
BEGIN
    PRINT 'openalex.PilotFacultyCohort does not exist yet. Run 190_PilotExpansion_Create_Cohort_Table.sql after TotalFaculty reaches 15.';
END
