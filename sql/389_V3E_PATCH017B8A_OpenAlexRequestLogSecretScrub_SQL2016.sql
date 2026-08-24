/*
V3E PATCH017B8A - OpenAlex Request-Log Secret Scrub
SQL Server 2016 compatible.

Purpose
-------
Remove historical raw OpenAlex request URLs from openalex.RequestLog when a
masked counterpart is already available. This does not alter ingestion data,
identity candidates, match decisions, publication candidates, or publication
decisions.

Why
---
Older ingestion jobs wrote the full request URI to RequestUrl and a masked copy
to RequestUrlMasked. Because OpenAlex API credentials are query-string values,
RequestUrl must not retain the raw credential.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'openalex.RequestLog', N'U') IS NULL
BEGIN
    RAISERROR('openalex.RequestLog does not exist.', 16, 1);
    RETURN;
END;

DECLARE @CandidateRows INT =
(
    SELECT COUNT(*)
    FROM openalex.RequestLog
    WHERE RequestUrlMasked IS NOT NULL
      AND RequestUrl IS NOT NULL
      AND RequestUrl <> RequestUrlMasked
      AND
      (
          CHARINDEX('api_key=', RequestUrl) > 0
          OR CHARINDEX('mailto=', RequestUrl) > 0
      )
);

BEGIN TRANSACTION;

UPDATE openalex.RequestLog
SET RequestUrl = RequestUrlMasked
WHERE RequestUrlMasked IS NOT NULL
  AND RequestUrl IS NOT NULL
  AND RequestUrl <> RequestUrlMasked
  AND
  (
      CHARINDEX('api_key=', RequestUrl) > 0
      OR CHARINDEX('mailto=', RequestUrl) > 0
  );

DECLARE @RowsScrubbed INT = @@ROWCOUNT;

IF @RowsScrubbed <> @CandidateRows
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('PATCH017B8A request-log scrub row-count verification failed. No changes were committed.', 16, 1);
    RETURN;
END;

COMMIT TRANSACTION;

SELECT
    CandidateRowsBeforeScrub = @CandidateRows,
    RowsScrubbed = @RowsScrubbed,
    RemainingRawApiKeyLikeRows =
    (
        SELECT COUNT(*)
        FROM openalex.RequestLog
        WHERE RequestUrl IS NOT NULL
          AND CHARINDEX('api_key=', RequestUrl) > 0
          AND CHARINDEX('api_key=masked', RequestUrl) = 0
          AND CHARINDEX('api_key=[REDACTED]', RequestUrl) = 0
    ),
    RemainingRawMailToLikeRows =
    (
        SELECT COUNT(*)
        FROM openalex.RequestLog
        WHERE RequestUrl IS NOT NULL
          AND CHARINDEX('mailto=', RequestUrl) > 0
          AND CHARINDEX('mailto=masked', RequestUrl) = 0
          AND CHARINDEX('mailto=[REDACTED]', RequestUrl) = 0
    ),
    SafetyStatus = 'RequestLogUrlScrubOnly_NoMatchingDataChanged';
