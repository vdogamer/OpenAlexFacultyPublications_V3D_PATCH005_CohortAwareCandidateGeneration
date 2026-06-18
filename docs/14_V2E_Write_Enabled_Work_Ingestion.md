# Version 2E Write-enabled Work/Publication Ingestion

## What Changed

Version 2E adds a new integrated worker job:

```text
WorkIngestion
```

The job:

1. Creates an `openalex.IngestionRun`.
2. Calls `/works` with:
   ```text
   filter=authorships.institutions.id:<institution IDs>
   ```
3. Logs each request into `openalex.RequestLog`.
4. Upserts works into `openalex.Work`.
5. Replaces authorships in `openalex.WorkAuthorship`.
6. Replaces authorship institutions in `openalex.WorkAuthorshipInstitution`.
7. Inserts configured institution match evidence in `openalex.WorkMatchedInstitution`.
8. Writes work counts into `openalex.IngestionRunSummary`.
9. Writes operational logs.

## Why Works After Authors?

Work rows and authorship evidence are needed before:

- publication candidate generation
- publication review queues
- accepted publication reports
- DOI/PubMed linking enhancements

## Safe Config

```json
"Worker": {
  "Enabled": true,
  "JobName": "WorkIngestion",
  "RunOnceAndStop": true
},
"WorkIngestion": {
  "RunMode": "TestLoad",
  "MaxPages": 1,
  "MaxRecords": 100
}
```

## Recommended Test

1. Run V2C institution ingestion.
2. Run V2D author ingestion.
3. Run V2E work ingestion.
4. Run `sql/081_V2E_Work_Ingestion_Demo_Queries.sql`.
5. Confirm `openalex.Work`, `openalex.WorkAuthorship`, and `openalex.WorkAuthorshipInstitution` rows exist.

## Important Design Note

Version 2E stores only selected relational fields plus the full raw JSON.

This preserves the earlier architecture decision: do not flatten every OpenAlex subfield unless needed for matching, reporting, or auditing.
