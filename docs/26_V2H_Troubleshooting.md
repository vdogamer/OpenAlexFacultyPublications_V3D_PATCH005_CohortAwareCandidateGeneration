# Version 2H Troubleshooting

## Admin Jobs Page Empty

Check whether `openalex.IngestionRun` exists. The job list is static, but recent run history requires the ingestion-run table.

## Access Denied

Use `admin.demo` for Admin pages.

Use `reviewer.demo` for Review pages.

Use `manager.demo` for Reports.

## Pipeline Fails

Inspect:

```sql
:r sql/111_V2H_Job_Run_Audit_Queries.sql
```

Also inspect recent operational logs:

```sql
SELECT TOP 100 *
FROM openalex.OperationalLog
ORDER BY OperationalLogId DESC;
```

## Shibboleth Mode Does Not Login

Confirm the expected headers are actually reaching ASP.NET Core:

- eppn
- uid
- displayName
- mail
- memberOf

Also confirm IIS/Shibboleth strips spoofed browser-supplied headers.
