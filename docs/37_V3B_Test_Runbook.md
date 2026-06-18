# Version 3B Test Runbook

## 1. Run SQL

```sql
:r sql/140_V3B_Create_Scheduler_Tables.sql
:r sql/141_V3B_Seed_Integrated_Jobs.sql
:r sql/142_V3B_Scheduler_Readiness_Check.sql
```

## 2. Start Web

```powershell
cd src\OpenAlex.Web
copy appsettings.example.json appsettings.json
dotnet run --urls "http://localhost:5030"
```

Login as `admin.demo`.

## 3. Queue a Safe Job

Open `/Admin/Scheduler` and queue `InstitutionIngestionPreview`.

## 4. Process Queue

Set worker:

```json
"Worker": {
  "Enabled": true,
  "JobName": "ProcessQueuedJobRequests",
  "RunOnceAndStop": true
}
```

Then run worker.

## 5. Verify

Open `/Admin/JobRequests` and `/Operations/Logs`, then run:

```sql
:r sql/143_V3B_Scheduler_Audit_Queries.sql
```
