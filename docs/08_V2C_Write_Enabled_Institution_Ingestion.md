# Version 2C Write-enabled Institution Ingestion

## What Changed

Version 2C replaces the safe preview-only worker job with a write-enabled institution ingestion job.

The new job:

1. Creates an `openalex.IngestionRun`.
2. Creates a `openalex.RequestLog` row for each OpenAlex request.
3. Calls `/institutions` using the configured institution ID filter.
4. Upserts institution rows into `openalex.Institution`.
5. Upserts `openalex.InstitutionIds`.
6. Writes `openalex.IngestionRunSummary`.
7. Logs operational events to `openalex.OperationalLog`.

## Why Only Institutions?

Institutions are the safest first write-enabled migration because:

- The configured institution list is small.
- The table was already proven in V1A.
- Institution IDs are stable.
- It validates the integrated worker without touching the larger author/work data yet.

## Config

```json
"Worker": {
  "Enabled": true,
  "JobName": "InstitutionIngestion",
  "RunOnceAndStop": true
},
"InstitutionIngestion": {
  "RunMode": "TestLoad",
  "MaxPages": 1,
  "MaxRecords": 10
}
```

## Safety

Keep `MaxPages` and `MaxRecords` small until the job is proven.

Do not use `FullLoad` until the team is ready for a broader data refresh.
