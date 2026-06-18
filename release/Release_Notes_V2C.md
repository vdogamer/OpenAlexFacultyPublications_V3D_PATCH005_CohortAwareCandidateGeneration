# Release Notes — Version 2C

## Release Name

Write-enabled Institution Ingestion

## Release Type

Integrated worker migration release.

## New

- `InstitutionIngestionRepository`
- `InstitutionIngestionJob`
- `InstitutionIngestionOptions`
- OpenAlex API client metadata capture
- IngestionRun lifecycle writes
- RequestLog writes
- Institution and InstitutionIds upsert
- IngestionRunSummary writes
- V2C validation/demo scripts
- V2D author ingestion backlog

## Safety

Worker remains disabled by default. Institution ingestion defaults to `TestLoad`, `MaxPages = 1`, and `MaxRecords = 10`.
