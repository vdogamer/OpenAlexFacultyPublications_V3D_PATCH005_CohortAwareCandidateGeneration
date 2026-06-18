# Version 2D Backlog — Author Ingestion Migration

## Goal

Migrate the write-enabled V1B author ingestion logic into the integrated worker.

## Planned Additions

- Author ingestion options
- Author ingestion job
- Author repository/upsert service
- `openalex.Author` upsert
- `openalex.AuthorLastKnownInstitution` upsert
- RequestLog rows for author API calls
- IngestionRunSummary author counts
- Author dashboard metrics
- Safety defaults for page/record limits

## Acceptance Criteria

- Worker can run `JobName = "AuthorIngestion"`.
- Authors are inserted/updated in `openalex.Author`.
- Author last-known institutions are stored.
- Request logs are written.
- Ingestion summary counts are written.
- Operational logs capture success/failure.
