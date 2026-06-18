/*
OpenAlex Version 2I — Final Smoke Test Report
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

PRINT 'Final smoke-test expected pages';

SELECT PagePath, RequiredRole, Purpose
FROM
(
    VALUES
        ('/', 'Reader', 'Dashboard'),
        ('/Faculty', 'Reader', 'Faculty list'),
        ('/Review/Authors', 'Reviewer', 'Author review'),
        ('/Review/Publications', 'Reviewer', 'Publication review'),
        ('/Reports/AcceptedPublications', 'Reader', 'Accepted publications report'),
        ('/Operations', 'Admin', 'Operational dashboard'),
        ('/Operations/Runs', 'Admin', 'Ingestion run table'),
        ('/Operations/Logs', 'Admin', 'Operational logs'),
        ('/Operations/Errors', 'Admin', 'Errors'),
        ('/Operations/Health', 'Admin', 'Health checks'),
        ('/Admin/Jobs', 'Admin', 'Job dashboard'),
        ('/Admin/Deployment', 'Admin', 'Deployment page'),
        ('/Admin/Users', 'Admin', 'Users/roles'),
        ('/Admin/AuthEvents', 'Admin', 'Authentication events')
) pages(PagePath, RequiredRole, Purpose);

PRINT 'Demo users';

IF OBJECT_ID(N'openalex.AppUser', N'U') IS NOT NULL
   AND OBJECT_ID(N'openalex.AppUserRole', N'U') IS NOT NULL
   AND OBJECT_ID(N'openalex.AppRole', N'U') IS NOT NULL
BEGIN
    SELECT
        u.UserName,
        u.DisplayName,
        u.Email,
        u.IsEnabled,
        r.RoleName
    FROM openalex.AppUser u
    LEFT JOIN openalex.AppUserRole ur
        ON u.AppUserId = ur.AppUserId
    LEFT JOIN openalex.AppRole r
        ON ur.AppRoleId = r.AppRoleId
    WHERE u.UserName IN ('manager.demo', 'reviewer.demo', 'admin.demo')
    ORDER BY u.UserName, r.RoleName;
END

PRINT 'Final accepted publication evidence';

IF OBJECT_ID(N'openalex.vw_FacultyPublicationExport', N'V') IS NOT NULL
BEGIN
    SELECT TOP 25 *
    FROM openalex.vw_FacultyPublicationExport
    ORDER BY FacultyDisplayName, PublicationYear DESC, WorkTitle;
END
ELSE IF OBJECT_ID(N'openalex.FacultyPublicationDecision', N'U') IS NOT NULL
BEGIN
    SELECT TOP 25 *
    FROM openalex.FacultyPublicationDecision
    WHERE DecisionStatus = 'Accepted'
      AND IsCurrent = 1
    ORDER BY ReviewedAtUtc DESC;
END
