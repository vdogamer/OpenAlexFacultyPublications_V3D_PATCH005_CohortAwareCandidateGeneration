# Release Notes — Version 2D

## Release Name

Write-enabled Author Ingestion

## Release Type

Integrated worker migration release.

## New

- `AuthorIngestionRepository`
- `AuthorUpsertResult`
- `AuthorIngestionOptions`
- `AuthorIngestionJob`
- Worker route for `JobName = "AuthorIngestion"`
- Author RequestLog writes
- Author and AuthorLastKnownInstitution upserts
- Author IngestionRunSummary counts
- V2D validation/demo scripts
- V2E Work ingestion backlog

## Safety

Worker remains disabled by default.

Author ingestion defaults to:

- RunMode: `TestLoad`
- MaxPages: `1`
- MaxRecords: `100`
