# Version 2A to Version 2B Backlog

## V2B Goal

Begin moving real working code from V1 packages into the integrated solution.

## Suggested GitHub Issues

### Issue 1 — Move SQL Connection Factory

Move the strongest SQL connection factory and shared helpers into `OpenAlex.Data`.

Acceptance criteria:

- Web app can resolve `SqlConnectionFactory`.
- Worker can resolve `SqlConnectionFactory`.
- Connection string comes from configuration.

### Issue 2 — Move Operational Logging Service

Move Version 1O logging service into `OpenAlex.OperationalLogging`.

Acceptance criteria:

- Web app can create an operational log row.
- Worker can create an operational log row.
- Application errors can be stored.

### Issue 3 — Move OpenAlex HTTP Client

Move V1A–V1C API logic into `OpenAlex.OpenAlexClient`.

Acceptance criteria:

- Can call institutions endpoint.
- Can call authors endpoint.
- Can call works endpoint.
- Cursor paging helper works.

### Issue 4 — Move Institution Ingestion Job

Create `InstitutionIngestionJob` in `OpenAlex.Worker`.

Acceptance criteria:

- Job can be called manually from worker.
- Job logs start/end.
- Job writes/upserts institution rows.

### Issue 5 — Move Review UI Pages

Move useful V1K/V1L review pages into `OpenAlex.Web`.

Acceptance criteria:

- Faculty list loads.
- Author review queue loads.
- Publication review queue loads.
- Accepted publication report loads.

### Issue 6 — Move Reporting Export

Move CSV export logic into `OpenAlex.Reporting`.

Acceptance criteria:

- Service exports accepted publications.
- Web app can download CSV.

## Rule for V2B

Small migrations. Build after every step.
