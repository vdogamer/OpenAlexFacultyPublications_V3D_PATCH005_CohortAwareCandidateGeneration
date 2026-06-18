# Version 2H Deployment Checklist

## Database

- [ ] Confirm correct database.
- [ ] Run all schema scripts through V1O/V1N/V1H as needed.
- [ ] Run V2H readiness scripts.
- [ ] Confirm demo/admin users.
- [ ] Confirm accepted publication views.

## Web App

- [ ] Configure production connection string securely.
- [ ] Set `Auth:Mode = ShibbolethHeaders`.
- [ ] Confirm IIS/Shibboleth header trust boundary.
- [ ] Deploy `OpenAlex.Web`.
- [ ] Confirm `/Account/Login` is not used in production mode.
- [ ] Confirm `/Admin/Jobs`.
- [ ] Confirm `/Reports/AcceptedPublications`.

## Worker

- [ ] Deploy `OpenAlex.Worker`.
- [ ] Keep `Worker:Enabled = false` by default.
- [ ] Run one small `TestLoad`.
- [ ] Review `openalex.IngestionRun`.
- [ ] Review `openalex.OperationalLog`.
- [ ] Increase limits only after approval.

## Security

- [ ] Confirm Reader role.
- [ ] Confirm Reviewer role.
- [ ] Confirm Admin role.
- [ ] Confirm unauthorized users are denied.
- [ ] Confirm review buttons require Reviewer/Admin.
- [ ] Confirm Operations/Admin pages require Admin.

## Demo

- [ ] Run `sql/112_V2H_Manager_Demo_Readiness_Report.sql`.
- [ ] Prepare at least one accepted author decision.
- [ ] Prepare at least one accepted publication decision.
- [ ] Prepare CSV export.
