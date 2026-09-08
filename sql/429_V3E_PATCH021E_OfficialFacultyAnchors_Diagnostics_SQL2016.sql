/*
V3E PATCH021E — Official Faculty Anchor Diagnostics
*/

SET NOCOUNT ON;

SELECT
    DepartmentDiscoveryAnchorId,
    ParentName,
    OfficialName,
    ScholarSearchText,
    ExpectedEmail,
    SearchPriority,
    IsActive,
    SourceNote
FROM academiconline.DepartmentDiscoveryAnchor
WHERE ParentName = N'School of Medicine'
ORDER BY
    OfficialName,
    SearchPriority,
    ScholarSearchText;
