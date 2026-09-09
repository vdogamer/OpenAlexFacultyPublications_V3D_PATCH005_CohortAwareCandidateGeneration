/*
V3E PATCH021M — Local Publication-Year Policy Enforcement
SQL Server 2016 compatible.

Purpose:
- Enforce department RefreshFromYear / RefreshToYear at the reporting boundary.
- Unknown or invalid numeric years are excluded whenever a department has a year policy.
- Preserve publication rows, YearText, RawJson, and non-department history.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'academiconline.Department', N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.Department does not exist. Apply PATCH019 first.',
        16,
        1);
    RETURN;
END;

IF OBJECT_ID(N'academiconline.Publication', N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.Publication does not exist. Apply PATCH019 first.',
        16,
        1);
    RETURN;
END;

IF OBJECT_ID(N'academiconline.vw_DepartmentNewestPublication', N'V') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.vw_DepartmentNewestPublication does not exist. Apply PATCH021J first.',
        16,
        1);
    RETURN;
END;

GO

EXEC(N'
ALTER VIEW academiconline.vw_DepartmentNewestPublication
AS
SELECT
    d.DepartmentId,
    d.CanonicalName AS DepartmentName,
    p.PublicationKey,
    p.AcademicOnlinePublicationId,
    p.OpenAlexId,
    p.Doi,
    p.Title,
    p.Venue,
    p.YearText,
    p.PublicationYear,
    p.CitationCount,
    p.Verified,
    p.Link,
    p.LastRetrievedAtUtc,

    FacultyCount =
        CASE
            WHEN d.SourceMode = N''FacultyRoster''
            THEN
            (
                SELECT COUNT(DISTINCT drp2.DepartmentRosterMemberId)
                FROM academiconline.DepartmentRosterPublication drp2
                WHERE drp2.DepartmentId = d.DepartmentId
                  AND drp2.PublicationKey = p.PublicationKey
                  AND drp2.IsCurrent = 1
                  AND drp2.CurrentSnapshotRunId = d.CurrentSnapshotRunId
            )
            ELSE
            (
                SELECT COUNT(DISTINCT dsp2.AcademicOnlineAuthorId)
                FROM academiconline.DepartmentScholarPublication dsp2
                WHERE dsp2.DepartmentId = d.DepartmentId
                  AND dsp2.PublicationKey = p.PublicationKey
                  AND dsp2.IsCurrent = 1
                  AND dsp2.CurrentSnapshotRunId = d.CurrentSnapshotRunId
            )
        END,

    FacultyNames =
        CASE
            WHEN d.SourceMode = N''FacultyRoster''
            THEN
                STUFF
                (
                    (
                        SELECT N''; '' + rm.DisplayName
                        FROM academiconline.DepartmentRosterPublication drp3
                        JOIN academiconline.DepartmentRosterMember rm
                            ON rm.DepartmentRosterMemberId =
                               drp3.DepartmentRosterMemberId
                        WHERE drp3.DepartmentId = d.DepartmentId
                          AND drp3.PublicationKey = p.PublicationKey
                          AND drp3.IsCurrent = 1
                          AND drp3.CurrentSnapshotRunId =
                              d.CurrentSnapshotRunId
                        GROUP BY rm.DisplayName
                        ORDER BY rm.DisplayName
                        FOR XML PATH(N''''), TYPE
                    ).value(N''.'', N''NVARCHAR(MAX)''),
                    1,
                    2,
                    N''''
                )
            ELSE
                STUFF
                (
                    (
                        SELECT N''; '' + s2.DisplayName
                        FROM academiconline.DepartmentScholarPublication dsp3
                        JOIN academiconline.Scholar s2
                            ON s2.AcademicOnlineAuthorId =
                               dsp3.AcademicOnlineAuthorId
                        WHERE dsp3.DepartmentId = d.DepartmentId
                          AND dsp3.PublicationKey = p.PublicationKey
                          AND dsp3.IsCurrent = 1
                          AND dsp3.CurrentSnapshotRunId =
                              d.CurrentSnapshotRunId
                        GROUP BY s2.DisplayName
                        ORDER BY s2.DisplayName
                        FOR XML PATH(N''''), TYPE
                    ).value(N''.'', N''NVARCHAR(MAX)''),
                    1,
                    2,
                    N''''
                )
        END

FROM academiconline.Department d
JOIN academiconline.DepartmentPublication dp
    ON dp.DepartmentId = d.DepartmentId
   AND dp.IsCurrent = 1
   AND dp.CurrentSnapshotRunId = d.CurrentSnapshotRunId
JOIN academiconline.Publication p
    ON p.PublicationKey = dp.PublicationKey
WHERE d.IsActive = 1
  AND d.CurrentSnapshotRunId IS NOT NULL
  AND
  (
      (
          d.RefreshFromYear IS NULL
          AND d.RefreshToYear IS NULL
      )
      OR
      (
          p.PublicationYear IS NOT NULL
          AND
          (
              d.RefreshFromYear IS NULL
              OR p.PublicationYear >= d.RefreshFromYear
          )
          AND
          (
              d.RefreshToYear IS NULL
              OR p.PublicationYear <= d.RefreshToYear
          )
      )
  );
');

GO

PRINT N'PATCH021M local publication-year policy reporting guard is ready.';
