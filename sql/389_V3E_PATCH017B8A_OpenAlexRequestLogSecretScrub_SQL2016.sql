/*
V3E PATCH017B8A - OpenAlex Request-Log Secret Scrub
SQL Server 2016 compatible.

Purpose
-------
Redact historical OpenAlex api_key and mailto query-parameter values directly
inside BOTH openalex.RequestLog.RequestUrl and RequestUrlMasked.

Earlier request-log rows can contain the raw query parameters in both columns,
so this script does not assume RequestUrlMasked is already safe.

This script does not alter ingestion data, identity candidates, match decisions,
publication candidates, or publication decisions.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'openalex.RequestLog', N'U') IS NULL
BEGIN
    RAISERROR('openalex.RequestLog does not exist.', 16, 1);
    RETURN;
END;

DECLARE @RowsWithRawDataBefore INT =
(
    SELECT COUNT(*)
    FROM openalex.RequestLog
    WHERE
        (
            RequestUrl IS NOT NULL
            AND
            (
                (CHARINDEX('api_key=', RequestUrl) > 0
                 AND CHARINDEX('api_key=masked', RequestUrl) = 0
                 AND CHARINDEX('api_key=[REDACTED]', RequestUrl) = 0)
                OR
                (CHARINDEX('mailto=', RequestUrl) > 0
                 AND CHARINDEX('mailto=masked', RequestUrl) = 0
                 AND CHARINDEX('mailto=[REDACTED]', RequestUrl) = 0)
            )
        )
        OR
        (
            RequestUrlMasked IS NOT NULL
            AND
            (
                (CHARINDEX('api_key=', RequestUrlMasked) > 0
                 AND CHARINDEX('api_key=masked', RequestUrlMasked) = 0
                 AND CHARINDEX('api_key=[REDACTED]', RequestUrlMasked) = 0)
                OR
                (CHARINDEX('mailto=', RequestUrlMasked) > 0
                 AND CHARINDEX('mailto=masked', RequestUrlMasked) = 0
                 AND CHARINDEX('mailto=[REDACTED]', RequestUrlMasked) = 0)
            )
        )
);

BEGIN TRANSACTION;

/* RequestUrl: api_key */
UPDATE openalex.RequestLog
SET RequestUrl = STUFF
(
    RequestUrl,
    CHARINDEX('api_key=', RequestUrl) + LEN('api_key='),
    CASE
        WHEN CHARINDEX('&', RequestUrl, CHARINDEX('api_key=', RequestUrl) + LEN('api_key=')) > 0
            THEN CHARINDEX('&', RequestUrl, CHARINDEX('api_key=', RequestUrl) + LEN('api_key='))
                 - (CHARINDEX('api_key=', RequestUrl) + LEN('api_key='))
        ELSE LEN(RequestUrl) - (CHARINDEX('api_key=', RequestUrl) + LEN('api_key=')) + 1
    END,
    'masked'
)
WHERE RequestUrl IS NOT NULL
  AND CHARINDEX('api_key=', RequestUrl) > 0
  AND CHARINDEX('api_key=masked', RequestUrl) = 0
  AND CHARINDEX('api_key=[REDACTED]', RequestUrl) = 0;

DECLARE @RequestUrlApiKeyRows INT = @@ROWCOUNT;

/* RequestUrl: mailto */
UPDATE openalex.RequestLog
SET RequestUrl = STUFF
(
    RequestUrl,
    CHARINDEX('mailto=', RequestUrl) + LEN('mailto='),
    CASE
        WHEN CHARINDEX('&', RequestUrl, CHARINDEX('mailto=', RequestUrl) + LEN('mailto=')) > 0
            THEN CHARINDEX('&', RequestUrl, CHARINDEX('mailto=', RequestUrl) + LEN('mailto='))
                 - (CHARINDEX('mailto=', RequestUrl) + LEN('mailto='))
        ELSE LEN(RequestUrl) - (CHARINDEX('mailto=', RequestUrl) + LEN('mailto=')) + 1
    END,
    'masked'
)
WHERE RequestUrl IS NOT NULL
  AND CHARINDEX('mailto=', RequestUrl) > 0
  AND CHARINDEX('mailto=masked', RequestUrl) = 0
  AND CHARINDEX('mailto=[REDACTED]', RequestUrl) = 0;

DECLARE @RequestUrlMailToRows INT = @@ROWCOUNT;

/* RequestUrlMasked: api_key */
UPDATE openalex.RequestLog
SET RequestUrlMasked = STUFF
(
    RequestUrlMasked,
    CHARINDEX('api_key=', RequestUrlMasked) + LEN('api_key='),
    CASE
        WHEN CHARINDEX('&', RequestUrlMasked, CHARINDEX('api_key=', RequestUrlMasked) + LEN('api_key=')) > 0
            THEN CHARINDEX('&', RequestUrlMasked, CHARINDEX('api_key=', RequestUrlMasked) + LEN('api_key='))
                 - (CHARINDEX('api_key=', RequestUrlMasked) + LEN('api_key='))
        ELSE LEN(RequestUrlMasked) - (CHARINDEX('api_key=', RequestUrlMasked) + LEN('api_key=')) + 1
    END,
    'masked'
)
WHERE RequestUrlMasked IS NOT NULL
  AND CHARINDEX('api_key=', RequestUrlMasked) > 0
  AND CHARINDEX('api_key=masked', RequestUrlMasked) = 0
  AND CHARINDEX('api_key=[REDACTED]', RequestUrlMasked) = 0;

DECLARE @RequestUrlMaskedApiKeyRows INT = @@ROWCOUNT;

/* RequestUrlMasked: mailto */
UPDATE openalex.RequestLog
SET RequestUrlMasked = STUFF
(
    RequestUrlMasked,
    CHARINDEX('mailto=', RequestUrlMasked) + LEN('mailto='),
    CASE
        WHEN CHARINDEX('&', RequestUrlMasked, CHARINDEX('mailto=', RequestUrlMasked) + LEN('mailto=')) > 0
            THEN CHARINDEX('&', RequestUrlMasked, CHARINDEX('mailto=', RequestUrlMasked) + LEN('mailto='))
                 - (CHARINDEX('mailto=', RequestUrlMasked) + LEN('mailto='))
        ELSE LEN(RequestUrlMasked) - (CHARINDEX('mailto=', RequestUrlMasked) + LEN('mailto=')) + 1
    END,
    'masked'
)
WHERE RequestUrlMasked IS NOT NULL
  AND CHARINDEX('mailto=', RequestUrlMasked) > 0
  AND CHARINDEX('mailto=masked', RequestUrlMasked) = 0
  AND CHARINDEX('mailto=[REDACTED]', RequestUrlMasked) = 0;

DECLARE @RequestUrlMaskedMailToRows INT = @@ROWCOUNT;

DECLARE @RemainingRawApiKeyRequestUrl INT =
(
    SELECT COUNT(*)
    FROM openalex.RequestLog
    WHERE RequestUrl IS NOT NULL
      AND CHARINDEX('api_key=', RequestUrl) > 0
      AND CHARINDEX('api_key=masked', RequestUrl) = 0
      AND CHARINDEX('api_key=[REDACTED]', RequestUrl) = 0
);

DECLARE @RemainingRawMailToRequestUrl INT =
(
    SELECT COUNT(*)
    FROM openalex.RequestLog
    WHERE RequestUrl IS NOT NULL
      AND CHARINDEX('mailto=', RequestUrl) > 0
      AND CHARINDEX('mailto=masked', RequestUrl) = 0
      AND CHARINDEX('mailto=[REDACTED]', RequestUrl) = 0
);

DECLARE @RemainingRawApiKeyRequestUrlMasked INT =
(
    SELECT COUNT(*)
    FROM openalex.RequestLog
    WHERE RequestUrlMasked IS NOT NULL
      AND CHARINDEX('api_key=', RequestUrlMasked) > 0
      AND CHARINDEX('api_key=masked', RequestUrlMasked) = 0
      AND CHARINDEX('api_key=[REDACTED]', RequestUrlMasked) = 0
);

DECLARE @RemainingRawMailToRequestUrlMasked INT =
(
    SELECT COUNT(*)
    FROM openalex.RequestLog
    WHERE RequestUrlMasked IS NOT NULL
      AND CHARINDEX('mailto=', RequestUrlMasked) > 0
      AND CHARINDEX('mailto=masked', RequestUrlMasked) = 0
      AND CHARINDEX('mailto=[REDACTED]', RequestUrlMasked) = 0
);

IF @RemainingRawApiKeyRequestUrl <> 0
   OR @RemainingRawMailToRequestUrl <> 0
   OR @RemainingRawApiKeyRequestUrlMasked <> 0
   OR @RemainingRawMailToRequestUrlMasked <> 0
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('PATCH017B8A request-log scrub verification failed. Raw query-parameter data remains, so no changes were committed.', 16, 1);
    RETURN;
END;

COMMIT TRANSACTION;

SELECT
    RowsWithRawDataBefore = @RowsWithRawDataBefore,
    RequestUrlApiKeyValuesRedacted = @RequestUrlApiKeyRows,
    RequestUrlMailToValuesRedacted = @RequestUrlMailToRows,
    RequestUrlMaskedApiKeyValuesRedacted = @RequestUrlMaskedApiKeyRows,
    RequestUrlMaskedMailToValuesRedacted = @RequestUrlMaskedMailToRows,
    RemainingRawApiKeyRowsInRequestUrl = @RemainingRawApiKeyRequestUrl,
    RemainingRawMailToRowsInRequestUrl = @RemainingRawMailToRequestUrl,
    RemainingRawApiKeyRowsInRequestUrlMasked = @RemainingRawApiKeyRequestUrlMasked,
    RemainingRawMailToRowsInRequestUrlMasked = @RemainingRawMailToRequestUrlMasked,
    SafetyStatus = 'RequestLogQueryParametersRedacted_NoMatchingDataChanged';
