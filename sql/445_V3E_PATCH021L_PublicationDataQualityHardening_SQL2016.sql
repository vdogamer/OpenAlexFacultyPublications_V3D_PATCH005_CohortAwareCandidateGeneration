/*
V3E PATCH021L — Publication Data Quality Hardening
SQL Server 2016 compatible.

Purpose:
- Normalize invalid publication-year and citation sentinels already stored.
- Prevent future invalid numeric values at the database boundary.
- Preserve upstream YearText and RawJson for provenance.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'academiconline.Publication', N'U') IS NULL
BEGIN
    RAISERROR(
        N'academiconline.Publication does not exist. Apply PATCH019 first.',
        16,
        1);
    RETURN;
END;

DECLARE @InvalidYearsBefore INT =
(
    SELECT COUNT(*)
    FROM academiconline.Publication
    WHERE PublicationYear IS NOT NULL
      AND
      (
          PublicationYear < 1000
          OR PublicationYear > 2100
      )
);

DECLARE @NegativeCitationsBefore INT =
(
    SELECT COUNT(*)
    FROM academiconline.Publication
    WHERE CitationCount < 0
);

UPDATE academiconline.Publication
SET PublicationYear = NULL
WHERE PublicationYear IS NOT NULL
  AND
  (
      PublicationYear < 1000
      OR PublicationYear > 2100
  );

UPDATE academiconline.Publication
SET CitationCount = NULL
WHERE CitationCount < 0;

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id =
          OBJECT_ID(N'academiconline.Publication')
      AND name =
          N'CK_academiconline_Publication_PublicationYear'
)
BEGIN
    ALTER TABLE academiconline.Publication
        DROP CONSTRAINT
        CK_academiconline_Publication_PublicationYear;
END;

ALTER TABLE academiconline.Publication
WITH CHECK
ADD CONSTRAINT
    CK_academiconline_Publication_PublicationYear
CHECK
(
    PublicationYear IS NULL
    OR PublicationYear BETWEEN 1000 AND 2100
);

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id =
          OBJECT_ID(N'academiconline.Publication')
      AND name =
          N'CK_academiconline_Publication_CitationCount'
)
BEGIN
    ALTER TABLE academiconline.Publication
        DROP CONSTRAINT
        CK_academiconline_Publication_CitationCount;
END;

ALTER TABLE academiconline.Publication
WITH CHECK
ADD CONSTRAINT
    CK_academiconline_Publication_CitationCount
CHECK
(
    CitationCount IS NULL
    OR CitationCount >= 0
);

SELECT
    InvalidYearsNormalized =
        @InvalidYearsBefore,
    NegativeCitationsNormalized =
        @NegativeCitationsBefore,
    RemainingInvalidYears =
    (
        SELECT COUNT(*)
        FROM academiconline.Publication
        WHERE PublicationYear IS NOT NULL
          AND
          (
              PublicationYear < 1000
              OR PublicationYear > 2100
          )
    ),
    RemainingNegativeCitations =
    (
        SELECT COUNT(*)
        FROM academiconline.Publication
        WHERE CitationCount < 0
    );

PRINT N'PATCH021L publication data-quality constraints are ready.';
