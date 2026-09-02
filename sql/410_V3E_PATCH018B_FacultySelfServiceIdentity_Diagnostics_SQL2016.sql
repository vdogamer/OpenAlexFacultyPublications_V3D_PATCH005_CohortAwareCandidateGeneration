/*
V3E PATCH018B - Faculty Self-Service Identity Resolution Diagnostics
Read-only. SQL Server 2016 compatible.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.FacultyAcademicOnlineAuthorMap', N'U') IS NULL
BEGIN
    RAISERROR('PATCH018 Academic Online mapping table is missing.', 16, 1);
    RETURN;
END;

PRINT '1) Enabled app users that exactly match faculty by email';
SELECT
    u.AppUserId,
    u.UserName,
    AppUserComputingId =
        CASE WHEN COL_LENGTH(N'openalex.AppUser', N'ComputingId') IS NULL
             THEN NULL
             ELSE u.ComputingId END,
    u.Email AS AppUserEmail,
    f.FacultyId,
    f.DisplayName AS FacultyDisplayName,
    f.PrimaryEmail AS FacultyEmail,
    HasCurrentAcademicOnlineMapping =
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM openalex.FacultyAcademicOnlineAuthorMap m
            WHERE m.FacultyId = f.FacultyId
              AND m.IsCurrent = 1
        ) THEN 1 ELSE 0 END
FROM openalex.AppUser u
INNER JOIN openalex.Faculty f
    ON LOWER(LTRIM(RTRIM(f.PrimaryEmail))) =
       LOWER(LTRIM(RTRIM(u.Email)))
WHERE u.IsEnabled = 1
ORDER BY f.DisplayName, u.UserName;

PRINT '2) Ambiguous app-user email matches';
SELECT
    u.AppUserId,
    u.UserName,
    u.Email,
    MatchingFacultyCount = COUNT(*)
FROM openalex.AppUser u
INNER JOIN openalex.Faculty f
    ON LOWER(LTRIM(RTRIM(f.PrimaryEmail))) =
       LOWER(LTRIM(RTRIM(u.Email)))
WHERE u.IsEnabled = 1
GROUP BY u.AppUserId, u.UserName, u.Email
HAVING COUNT(*) > 1
ORDER BY u.UserName;

PRINT '3) PATCH018B local demo readiness';
DECLARE @DemoUserName NVARCHAR(256) = N'faculty.demo.farber';

SELECT
    DemoUserExists =
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM openalex.AppUser
            WHERE UserName = @DemoUserName
              AND IsEnabled = 1
        ) THEN 1 ELSE 0 END,
    DemoUserMatchesExactlyOneFaculty =
        CASE WHEN
        (
            SELECT COUNT(*)
            FROM openalex.AppUser u
            INNER JOIN openalex.Faculty f
                ON LOWER(LTRIM(RTRIM(f.PrimaryEmail))) =
                   LOWER(LTRIM(RTRIM(u.Email)))
            WHERE u.UserName = @DemoUserName
              AND u.IsEnabled = 1
        ) = 1 THEN 1 ELSE 0 END,
    MatchedFacultyHasAcademicOnlineMapping =
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM openalex.AppUser u
            INNER JOIN openalex.Faculty f
                ON LOWER(LTRIM(RTRIM(f.PrimaryEmail))) =
                   LOWER(LTRIM(RTRIM(u.Email)))
            INNER JOIN openalex.FacultyAcademicOnlineAuthorMap m
                ON m.FacultyId = f.FacultyId
               AND m.IsCurrent = 1
            WHERE u.UserName = @DemoUserName
              AND u.IsEnabled = 1
        ) THEN 1 ELSE 0 END,
    NextAction =
        CASE
            WHEN NOT EXISTS
            (
                SELECT 1
                FROM openalex.AppUser
                WHERE UserName = @DemoUserName
                  AND IsEnabled = 1
            )
                THEN 'Run409LocalDemoFacultyLoginSeed'
            WHEN
            (
                SELECT COUNT(*)
                FROM openalex.AppUser u
                INNER JOIN openalex.Faculty f
                    ON LOWER(LTRIM(RTRIM(f.PrimaryEmail))) =
                       LOWER(LTRIM(RTRIM(u.Email)))
                WHERE u.UserName = @DemoUserName
                  AND u.IsEnabled = 1
            ) <> 1
                THEN 'ReviewDemoUserFacultyIdentityMatch'
            ELSE 'LoginAsFacultyDemoAndOpenMyPublications'
        END;
