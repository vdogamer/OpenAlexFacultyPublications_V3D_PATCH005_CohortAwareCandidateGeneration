# Version 2E Backlog — Work/Publication Ingestion Migration

## Goal

Migrate the write-enabled V1C work/publication ingestion logic into the integrated worker.

## Planned Additions

- `WorkIngestionOptions`
- `WorkIngestionJob`
- `WorkIngestionRepository`
- Work upsert into `openalex.Work`
- Work authorship upsert into `openalex.WorkAuthorship`
- Work authorship institution upsert into `openalex.WorkAuthorshipInstitution`
- Matched institution rows into `openalex.WorkMatchedInstitution`
- RequestLog rows for work API calls
- IngestionRunSummary work counts
- Web dashboard work-count confirmation

## Acceptance Criteria

- Worker can run `JobName = "WorkIngestion"`.
- Works are inserted/updated.
- Authorships are stored.
- Authorship institutions are stored.
- Request logs and run summaries are created.
- Operational logs show success/failure.
