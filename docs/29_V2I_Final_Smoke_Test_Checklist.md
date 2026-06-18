# Version 2I Final Smoke Test Checklist

## Build

- [ ] `dotnet clean`
- [ ] `dotnet restore`
- [ ] `dotnet build`

## Web

- [ ] Start web app on port 5028.
- [ ] Login as `admin.demo`.
- [ ] Open `/Operations`.
- [ ] Open `/Operations/Runs`.
- [ ] Open `/Operations/Logs`.
- [ ] Open `/Operations/Errors`.
- [ ] Open `/Operations/Health`.
- [ ] Open `/Admin/Jobs`.
- [ ] Open `/Admin/Deployment`.
- [ ] Open `/Reports/AcceptedPublications`.

## Worker

- [ ] Run one safe `InstitutionIngestion` TestLoad.
- [ ] Run one safe `RunPipelineOnce` TestLoad only after confirming small limits.
- [ ] Confirm recent run appears in `/Operations/Runs`.
- [ ] Confirm logs appear in `/Operations/Logs`.

## SQL

Run:

```sql
:r sql/120_V2I_Production_Readiness_Report.sql
:r sql/121_V2I_Operational_Dashboard_Audit.sql
:r sql/122_V2I_Final_Smoke_Test_Report.sql
```

## Review

- [ ] Reviewer can accept author candidate.
- [ ] Reviewer can accept publication candidate.
- [ ] Accepted Publications report shows accepted publication.
- [ ] CSV export works.
