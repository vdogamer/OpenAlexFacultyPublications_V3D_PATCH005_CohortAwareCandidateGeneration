/*
V3E PATCH021J — Mixed-Source Faculty-Roster Publication Retrieval
SQL Server 2016 compatible.

Purpose:
- Keep official FacultyRoster membership separate from provider-specific scholar identity.
- Attribute publications to roster members regardless of whether the publication
  was retrieved through Academic Online or OpenAlex.
- Preserve the existing snapshot safety gate.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'academiconline.DepartmentRosterMember', N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.DepartmentRosterMember does not exist. Apply PATCH021F first.',
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

IF OBJECT_ID(N'academiconline.DepartmentRosterPublication', N'U') IS NULL
BEGIN
    CREATE TABLE academiconline.DepartmentRosterPublication
    (
        DepartmentId BIGINT NOT NULL,
        DepartmentRosterMemberId BIGINT NOT NULL,
        PublicationKey BIGINT NOT NULL,
        SourceProvider NVARCHAR(30) NOT NULL,
        SourceAuthorId NVARCHAR(128) NOT NULL,
        IsCurrent BIT NOT NULL
            CONSTRAINT DF_academiconline_DepartmentRosterPublication_IsCurrent DEFAULT (0),
        FirstSeenAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_academiconline_DepartmentRosterPublication_FirstSeen DEFAULT (SYSUTCDATETIME()),
        LastSeenAtUtc DATETIME2(3) NOT NULL,
        LastSyncRunId BIGINT NULL,
        CurrentSnapshotRunId BIGINT NULL,

        CONSTRAINT PK_academiconline_DepartmentRosterPublication
            PRIMARY KEY
            (
                DepartmentId,
                DepartmentRosterMemberId,
                PublicationKey,
                SourceProvider
            ),

        CONSTRAINT FK_academiconline_DepartmentRosterPublication_Department
            FOREIGN KEY (DepartmentId)
            REFERENCES academiconline.Department(DepartmentId),

        CONSTRAINT FK_academiconline_DepartmentRosterPublication_RosterMember
            FOREIGN KEY (DepartmentRosterMemberId)
            REFERENCES academiconline.DepartmentRosterMember(DepartmentRosterMemberId),

        CONSTRAINT FK_academiconline_DepartmentRosterPublication_Publication
            FOREIGN KEY (PublicationKey)
            REFERENCES academiconline.Publication(PublicationKey),

        CONSTRAINT CK_academiconline_DepartmentRosterPublication_SourceProvider
            CHECK (SourceProvider IN (N'AcademicOnline', N'OpenAlex'))
    );

    CREATE INDEX IX_academiconline_DepartmentRosterPublication_Current
        ON academiconline.DepartmentRosterPublication
        (
            DepartmentId,
            IsCurrent,
            CurrentSnapshotRunId,
            PublicationKey,
            DepartmentRosterMemberId
        );

    CREATE INDEX IX_academiconline_DepartmentRosterPublication_Run
        ON academiconline.DepartmentRosterPublication
        (
            DepartmentId,
            LastSyncRunId,
            SourceProvider,
            DepartmentRosterMemberId,
            PublicationKey
        );
END;

GO

IF OBJECT_ID(N'academiconline.vw_DepartmentNewestPublication', N'V') IS NOT NULL
BEGIN
    DROP VIEW academiconline.vw_DepartmentNewestPublication;
END;

GO

EXEC(N'
CREATE VIEW academiconline.vw_DepartmentNewestPublication
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
  AND d.CurrentSnapshotRunId IS NOT NULL;
');

GO

PRINT N'PATCH021J mixed-source roster publication attribution is ready.';
