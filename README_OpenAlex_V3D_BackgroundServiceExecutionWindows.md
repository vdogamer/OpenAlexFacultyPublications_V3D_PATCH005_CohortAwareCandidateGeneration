# OpenAlex Version 3D — Background Service Deployment + Scheduled Execution Windows

## Purpose

Version 3D hardens the worker/background-service side of the OpenAlex Faculty Publications application.

Version 3C added role management and external Shibboleth group-to-role mapping. Version 3D adds a production-safety layer for scheduled execution windows, worker deployment as a Windows Service, and clearer operations runbooks.

## What Version 3D Adds

- Approved execution-window SQL model
- Execution-window seed/readiness/audit scripts
- Admin Execution Windows pages
- Worker execution-window options
- Worker queue-processing guidance for approved windows
- Windows Service deployment scripts
- Windows Service uninstall script
- Production worker appsettings template
- Background service runbook
- Version 3E backlog

## New SQL Object

```text
openalex.JobExecutionWindow
```

## New Admin Pages

```text
/Admin/ExecutionWindows
/Admin/EditExecutionWindow
```

All require the `Admin` role.

## Safety Design

Local/test defaults are permissive so smoke testing is not blocked.

Production should enable approved-window enforcement before processing queued jobs.

## SQL Setup

Run in SSMS:

```sql
:r sql/160_V3D_Create_Execution_Window_Tables.sql
:r sql/161_V3D_Seed_Default_Execution_Windows.sql
:r sql/162_V3D_Execution_Window_Readiness_Check.sql
:r sql/163_V3D_Execution_Window_Audit_Queries.sql
```

## Run Web App

```powershell
cd "C:\Users\Cameron\OneDrive - University of Virginia\Desktop\Smoke Test\OpenAlexFacultyPublications_V3D_BackgroundServiceExecutionWindows\src\OpenAlex.Web"
copy appsettings.example.json appsettings.json
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --urls "http://localhost:5032"
```

Open:

```text
http://localhost:5032
```

Login as:

```text
admin.demo
```

Then test:

```text
/Admin/ExecutionWindows
/Admin/Scheduler
/Admin/JobRequests
/Operations
```

## Production Worker Service

See:

```text
deployment/Version_3D_Background_Service_Runbook.md
scripts/install-worker-windows-service.ps1
scripts/uninstall-worker-windows-service.ps1
config/appsettings.worker.service.template.json
```

## Next Phase

Version 3E should add job cancellation, retry policies, and stronger max-load guardrails.


## PATCH001 — Controlled OpenAlex TestLoad Readiness

Before pulling real OpenAlex data, configure:

```json
"OpenAlex": {
  "ApiKey": "YOUR_OPENALEX_API_KEY"
}
```

Then follow:

```text
docs/45_Controlled_OpenAlex_TestLoad_Milestone.md
```


## PATCH002 — Manual Faculty Intake

Adds `/Admin/AddFaculty` so Admin users can add actual faculty rows needed for the 15-faculty pilot expansion.


## PATCH003 — Schema-Safe Manual Faculty Intake

Use this package instead of PATCH002 if `/Admin/AddFaculty` shows an unexpected error.

Run:

```sql
:r sql/197_ManualFacultyIntake_Schema_Check.sql
```

Then open:

```text
/Admin/AddFaculty
```


## PATCH004 — Review Queue Pending-Only Fix

Use this package if `/Review/Authors` keeps showing the same candidates after Accept/Reject.

After applying PATCH004, accepted/rejected candidates should disappear from the pending review queue.

Run:

```sql
:r sql/198_ReviewQueue_AfterAccept_Diagnostic.sql
```


## PATCH005 — Candidate Generation Honors PilotFacultyCohort

Use this patch when candidate generation needs to process the exact 15 enabled rows in `openalex.PilotFacultyCohort`.

Set:

```json
"CandidateGeneration": {
  "PilotCohortName": "Pilot Expansion 15 Faculty"
}
```

Run:

```sql
:r sql/199_PATCH005_CohortAwareCandidateGeneration_Diagnostic.sql
```
