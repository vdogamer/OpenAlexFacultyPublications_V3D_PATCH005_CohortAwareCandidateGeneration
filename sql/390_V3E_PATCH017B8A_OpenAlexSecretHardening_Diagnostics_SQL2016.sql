/*
V3E PATCH017B8A - OpenAlex Secret Hardening Diagnostics
SQL Server 2016 compatible.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'openalex.RequestLog', N'U') IS NULL
BEGIN
    RAISERROR('openalex.RequestLog does not exist.', 16, 1);
    RETURN;
END;

DECLARE @RawApiKeyRows INT =
(
    SELECT COUNT(*)
    FROM openalex.RequestLog
    WHERE RequestUrl IS NOT NULL
      AND CHARINDEX('api_key=', RequestUrl) > 0
      AND CHARINDEX('api_key=masked', RequestUrl) = 0
      AND CHARINDEX('api_key=[REDACTED]', RequestUrl) = 0
);

DECLARE @RawMailToRows INT =
(
    SELECT COUNT(*)
    FROM openalex.RequestLog
    WHERE RequestUrl IS NOT NULL
      AND CHARINDEX('mailto=', RequestUrl) > 0
      AND CHARINDEX('mailto=masked', RequestUrl) = 0
      AND CHARINDEX('mailto=[REDACTED]', RequestUrl) = 0
);

DECLARE @RawApiKeyMaskedColumnRows INT =
(
    SELECT COUNT(*)
    FROM openalex.RequestLog
    WHERE RequestUrlMasked IS NOT NULL
      AND CHARINDEX('api_key=', RequestUrlMasked) > 0
      AND CHARINDEX('api_key=masked', RequestUrlMasked) = 0
      AND CHARINDEX('api_key=[REDACTED]', RequestUrlMasked) = 0
);

SELECT TOP (20)
    RequestLogId,
    IngestionRunId,
    EntityType,
    RequestUrl,
    RequestUrlMasked,
    RequestSucceeded,
    StartedAtUtc
FROM openalex.RequestLog
ORDER BY RequestLogId DESC;

SELECT
    RawApiKeyRowsInRequestUrl = @RawApiKeyRows,
    RawMailToRowsInRequestUrl = @RawMailToRows,
    RawApiKeyRowsInRequestUrlMasked = @RawApiKeyMaskedColumnRows,
    SecurityStatus =
        CASE
            WHEN @RawApiKeyRows = 0
             AND @RawMailToRows = 0
             AND @RawApiKeyMaskedColumnRows = 0
                THEN 'DatabaseRequestUrlsRedacted'
            ELSE 'ActionRequired_UnredactedRequestUrlDataRemains'
        END,
    NextAction =
        CASE
            WHEN @RawApiKeyRows = 0
             AND @RawMailToRows = 0
             AND @RawApiKeyMaskedColumnRows = 0
                THEN 'RotateExposedOpenAlexKey_SetRuntimeSecret_BuildThenResumeCohortWorkExpansion'
            ELSE 'Run389ThenRecheck390'
        END;
