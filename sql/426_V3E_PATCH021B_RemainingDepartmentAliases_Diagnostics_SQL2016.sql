/*
V3E PATCH021B — Remaining Department Alias Diagnostics
*/

SET NOCOUNT ON;

SELECT
    OfficialName,
    AcademicOnlineSearchName,
    ValidationParentName =
        COALESCE(ValidationParentName, ParentName),
    DepartmentCategory,
    SearchPriority,
    IsActive,
    SourceNote
FROM academiconline.DepartmentDiscoverySeed
WHERE ParentName = N'School of Medicine'
  AND OfficialName IN
  (
      N'Biomedical Engineering',
      N'Medicine',
      N'Neurosurgery',
      N'Orthopaedic Surgery',
      N'Otolaryngology'
  )
ORDER BY
    OfficialName,
    SearchPriority,
    AcademicOnlineSearchName;

SELECT
    OfficialName,
    AliasCount = COUNT(*)
FROM academiconline.DepartmentDiscoverySeed
WHERE ParentName = N'School of Medicine'
  AND IsActive = 1
  AND OfficialName IN
  (
      N'Biomedical Engineering',
      N'Medicine',
      N'Neurosurgery',
      N'Orthopaedic Surgery',
      N'Otolaryngology'
  )
GROUP BY OfficialName
ORDER BY OfficialName;
