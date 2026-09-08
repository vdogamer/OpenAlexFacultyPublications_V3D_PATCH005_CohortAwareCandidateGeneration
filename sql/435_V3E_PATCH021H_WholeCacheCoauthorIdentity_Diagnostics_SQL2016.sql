/*
V3E PATCH021H — Whole-Cache Coauthor Identity Diagnostic
SQL Server 2016 compatible.

Purpose:
- Search the entire local Academic Online publication cache for unresolved
  Neurosurgery roster members.
- Surface non-null Academic Online coauthor IDs and evidence counts.
- Do not modify any data.
*/

SET NOCOUNT ON;

DECLARE @DepartmentId BIGINT =
(
    SELECT TOP (1) DepartmentId
    FROM academiconline.Department
    WHERE CanonicalName = N'Department of Neurosurgery'
      AND IsActive = 1
    ORDER BY DepartmentId
);

DECLARE @Target TABLE
(
    DisplayName NVARCHAR(512) NOT NULL,
    Token1 NVARCHAR(128) NOT NULL,
    Token2 NVARCHAR(128) NOT NULL,
    VariantPriority INT NOT NULL
);

INSERT INTO @Target
VALUES
(N'Aaron Dumont',       N'aaron',   N'dumont', 1),
(N'W. Jeffrey Elias',   N'jeffrey', N'elias',  1),
(N'Paul Grabb',         N'paul',    N'grabb',  1),
(N'Elisa Kucia',        N'elisa',   N'kucia',  1),
(N'Melike Mut Askun',   N'melike',  N'askun',  1),
(N'Melike Mut Askun',   N'melike',  N'mut',    2);

;WITH Parsed AS
(
    SELECT
        p.PublicationKey,
        p.PublicationYear,
        p.OpenAlexId,
        p.Doi,
        CoauthorName = j.[name],
        AcademicOnlineAuthorId = j.[id]
    FROM academiconline.Publication p
    CROSS APPLY OPENJSON(p.CoauthorsJson)
    WITH
    (
        [name] NVARCHAR(512) '$.name',
        [id]   NVARCHAR(128) '$.id'
    ) j
    WHERE p.CoauthorsJson IS NOT NULL
      AND ISJSON(p.CoauthorsJson) = 1
),
Evidence AS
(
    SELECT
        t.DisplayName,
        t.VariantPriority,
        p.CoauthorName,
        p.AcademicOnlineAuthorId,
        p.PublicationKey,
        p.PublicationYear
    FROM @Target t
    JOIN Parsed p
      ON LOWER(p.CoauthorName) LIKE N'%' + t.Token1 + N'%'
     AND LOWER(p.CoauthorName) LIKE N'%' + t.Token2 + N'%'
)
SELECT
    e.DisplayName,
    e.CoauthorName,
    e.AcademicOnlineAuthorId,
    EvidencePublicationCount =
        COUNT(DISTINCT e.PublicationKey),
    EarliestPublicationYear =
        MIN(e.PublicationYear),
    LatestPublicationYear =
        MAX(e.PublicationYear),
    LocalScholarFound =
        CASE
            WHEN s.AcademicOnlineAuthorId IS NULL THEN 0
            ELSE 1
        END,
    LocalScholarDisplayName = s.DisplayName,
    LocalScholarEmail = s.Email,
    LocalScholarInstitution = s.Institution
FROM Evidence e
LEFT JOIN academiconline.Scholar s
    ON s.AcademicOnlineAuthorId = e.AcademicOnlineAuthorId
GROUP BY
    e.DisplayName,
    e.CoauthorName,
    e.AcademicOnlineAuthorId,
    s.AcademicOnlineAuthorId,
    s.DisplayName,
    s.Email,
    s.Institution
ORDER BY
    e.DisplayName,
    CASE WHEN e.AcademicOnlineAuthorId IS NULL THEN 1 ELSE 0 END,
    EvidencePublicationCount DESC,
    e.CoauthorName;

SELECT
    r.DisplayName,
    r.ExpectedEmail,
    r.AcademicOnlineAuthorId,
    r.ResolutionStatus,
    r.ResolutionError
FROM academiconline.DepartmentRosterMember r
WHERE r.DepartmentId = @DepartmentId
  AND r.IsActive = 1
ORDER BY
    CASE WHEN r.AcademicOnlineAuthorId IS NULL THEN 0 ELSE 1 END,
    r.SearchPriority,
    r.DisplayName;
