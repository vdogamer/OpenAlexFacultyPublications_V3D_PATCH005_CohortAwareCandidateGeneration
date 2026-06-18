# Version 2C Troubleshooting

## Error: CK_IngestionRun_RunMode

Use one of the V1A-supported values:

- SmokeTest
- TestLoad
- FullLoad

Version 2C defaults to `TestLoad`.

## Error: Invalid object name openalex.Institution

Run the V1A schema first.

## Error: Invalid object name openalex.OperationalLog

Run the V1O operational logging schema, or temporarily set:

```json
"OperationalLogging": {
  "EnableDatabaseLogging": false
}
```

## Error: OpenAlex institution row is missing required id

This means the response shape was not the expected OpenAlex institution response. Confirm the configured endpoint/filter.

## Generic worker failure

Run with visible console logs and then inspect:

```sql
:r sql/061_V2C_Institution_Ingestion_Demo_Queries.sql
```

Also inspect:

```sql
SELECT TOP 50 *
FROM openalex.OperationalLog
ORDER BY OperationalLogId DESC;
```
