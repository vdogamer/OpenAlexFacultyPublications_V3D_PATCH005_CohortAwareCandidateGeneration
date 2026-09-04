/*
V3E PATCH021A — Official Department Seed Diagnostics
*/

SET NOCOUNT ON;

SELECT
    ParentName,
    DepartmentCategory,
    OfficialDepartmentCount =
        COUNT(DISTINCT OfficialName),
    ActiveSearchVariantCount =
        SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END)
FROM academiconline.DepartmentDiscoverySeed
GROUP BY
    ParentName,
    DepartmentCategory
ORDER BY
    ParentName,
    DepartmentCategory;

SELECT
    DepartmentDiscoverySeedId,
    ParentName,
    DepartmentCategory,
    OfficialName,
    AcademicOnlineSearchName,
    SearchPriority,
    IsActive,
    SourceUrl
FROM academiconline.DepartmentDiscoverySeed
ORDER BY
    DepartmentCategory,
    OfficialName,
    SearchPriority;
