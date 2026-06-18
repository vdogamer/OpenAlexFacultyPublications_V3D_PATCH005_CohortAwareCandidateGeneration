/*
OpenAlex Version 2G — Demo User Verification
*/

SET NOCOUNT ON;

DECLARE @Expected TABLE
(
    UserName NVARCHAR(256),
    RoleName NVARCHAR(100)
);

INSERT INTO @Expected (UserName, RoleName)
VALUES
    (N'manager.demo', N'Reader'),
    (N'reviewer.demo', N'Reader'),
    (N'reviewer.demo', N'Reviewer'),
    (N'admin.demo', N'Reader'),
    (N'admin.demo', N'Reviewer'),
    (N'admin.demo', N'Admin');

SELECT
    e.UserName,
    e.RoleName,
    ExistsFlag =
        CASE WHEN ur.AppUserRoleId IS NULL THEN 0 ELSE 1 END
FROM @Expected e
LEFT JOIN openalex.AppUser u
    ON e.UserName = u.UserName
LEFT JOIN openalex.AppRole r
    ON e.RoleName = r.RoleName
LEFT JOIN openalex.AppUserRole ur
    ON u.AppUserId = ur.AppUserId
   AND r.AppRoleId = ur.AppRoleId
ORDER BY e.UserName, e.RoleName;
