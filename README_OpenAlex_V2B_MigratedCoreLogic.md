# OpenAlex Version 2B — Migrated Core Logic

## Purpose

Version 2B starts moving the working Version 1 prototype behavior into the integrated Version 2 solution.

Version 2A created the integrated solution skeleton. Version 2B begins using the real V1 database tables/views from the integrated web app and worker host.

## What Version 2B Adds

- Integrated dashboard reads real OpenAlex/V1 tables.
- Faculty page reads `openalex.Faculty`.
- Author review queue reads candidate/decision tables.
- Publication review queue reads V1H reporting views.
- Accepted publications report reads `openalex.vw_FacultyPublicationExport`.
- CSV export endpoint is wired into the integrated web app.
- Operations page reads `openalex.OperationalLog`.
- Worker host includes an institution-ingestion preview job.
- Worker logs through the shared operational logging library.
- V2B SQL readiness and demo scripts.
- Migration notes for porting the remaining V1 logic.

## Important Safety Boundary

The Version 2B worker job is a **preview job** by default.

It calls the OpenAlex API and logs result counts, but does not upsert institution rows yet. This prevents accidental data mutation while we are consolidating the solution.

Full write-enabled ingestion should be added in Version 2C after the integrated data services are validated.

## Run SQL Readiness

In SSMS:

```sql
:r sql/050_V2B_Migrated_Core_Readiness_Check.sql
:r sql/051_V2B_Demo_Queries.sql
```

## Run Web App

```powershell
cd "C:\Users\Cameron\OneDrive - University of Virginia\Desktop\Smoke Test\OpenAlexFacultyPublications_V2B_MigratedCoreLogic\src\OpenAlex.Web"

copy appsettings.example.json appsettings.json

dotnet clean
dotnet restore
dotnet build
dotnet run --urls "http://localhost:5021"
```

Open:

```text
http://localhost:5021
```

## Run Worker Host

```powershell
cd "C:\Users\Cameron\OneDrive - University of Virginia\Desktop\Smoke Test\OpenAlexFacultyPublications_V2B_MigratedCoreLogic\src\OpenAlex.Worker"

copy appsettings.example.json appsettings.json

dotnet clean
dotnet restore
dotnet build
dotnet run
```

The worker remains disabled unless you set:

```json
"Worker": {
  "Enabled": true,
  "JobName": "InstitutionIngestionPreview"
}
```

## Suggested Test Order

1. Build the solution.
2. Run SQL readiness script.
3. Run the web app.
4. Open Dashboard.
5. Open Faculty.
6. Open Author Review.
7. Open Publication Review.
8. Open Accepted Publications.
9. Download CSV.
10. Open Operations.
11. Enable worker preview and confirm logs appear.

## Next Phase

Version 2C should migrate write-enabled ingestion services:

- Institution upsert
- Author upsert
- Work upsert
- Request logging
- Ingestion run logging
- Cursor paging
- Retry/backoff
