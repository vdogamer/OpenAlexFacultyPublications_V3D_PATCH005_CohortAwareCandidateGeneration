# Version 2D Write-enabled Author Ingestion

## What Changed

Version 2D adds a new integrated worker job:

```text
AuthorIngestion
```

The job:

1. Creates an `openalex.IngestionRun`.
2. Calls `/authors` with:
   ```text
   filter=last_known_institutions.id:<institution IDs>
   ```
3. Logs each request into `openalex.RequestLog`.
4. Upserts authors into `openalex.Author`.
5. Replaces each author's last-known institution evidence in `openalex.AuthorLastKnownInstitution`.
6. Writes author counts into `openalex.IngestionRunSummary`.
7. Writes operational logs.

## Why Authors After Institutions?

Author rows are needed before:

- faculty-author candidate generation
- publication candidate generation
- work-authorship matching

## Safe Config

```json
"Worker": {
  "Enabled": true,
  "JobName": "AuthorIngestion",
  "RunOnceAndStop": true
},
"AuthorIngestion": {
  "RunMode": "TestLoad",
  "MaxPages": 1,
  "MaxRecords": 100
}
```

## Recommended Test

1. Run V2C institution ingestion first.
2. Run V2D author ingestion.
3. Run `sql/071_V2D_Author_Ingestion_Demo_Queries.sql`.
4. Confirm `openalex.Author` and `openalex.AuthorLastKnownInstitution` rows exist.
