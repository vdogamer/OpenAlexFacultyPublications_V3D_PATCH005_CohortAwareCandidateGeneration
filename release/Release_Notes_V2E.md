# Release Notes — Version 2E

## Release Name

Write-enabled Work/Publication Ingestion

## Release Type

Integrated worker migration release.

## New

- `WorkIngestionRepository`
- `WorkUpsertResult`
- `WorkIngestionOptions`
- `WorkIngestionJob`
- Worker route for `JobName = "WorkIngestion"`
- Work RequestLog writes
- Work upsert
- WorkAuthorship replacement
- WorkAuthorshipInstitution replacement
- WorkMatchedInstitution evidence insertion
- Work IngestionRunSummary counts
- V2E validation/demo scripts
- V2F candidate-generation/review backlog

## Safety

Worker remains disabled by default.

Work ingestion defaults to:

- RunMode: `TestLoad`
- MaxPages: `1`
- MaxRecords: `100`
