# OpenAlex Faculty Publications — Version 2B Integrated Application

This package is the Version 2B integrated solution.

Version 2A created the solution shell. Version 2B starts migrating real V1 behavior into the integrated app.

## Projects

```text
OpenAlexFacultyPublications.sln
src/
├── OpenAlex.Web
├── OpenAlex.Worker
├── OpenAlex.Data
├── OpenAlex.OpenAlexClient
├── OpenAlex.Matching
├── OpenAlex.Reporting
└── OpenAlex.OperationalLogging
```

## Version 2B Highlights

- Dashboard reads real V1 OpenAlex table counts.
- Faculty page reads `openalex.Faculty`.
- Review pages read existing candidate/reporting views.
- Accepted publications report and CSV export are wired.
- Operations page reads operational logs.
- Worker includes a safe OpenAlex institution ingestion preview job.

## Build

```powershell
dotnet clean
dotnet restore
dotnet build
```

## Run Web App

```powershell
cd src/OpenAlex.Web
copy appsettings.example.json appsettings.json
dotnet run --urls "http://localhost:5021"
```

## Run Worker

```powershell
cd src/OpenAlex.Worker
copy appsettings.example.json appsettings.json
dotnet run
```

## Database

Run:

```sql
:r sql/050_V2B_Migrated_Core_Readiness_Check.sql
:r sql/051_V2B_Demo_Queries.sql
```
