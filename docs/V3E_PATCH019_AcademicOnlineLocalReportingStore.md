# V3E PATCH019 — Academic Online Local Reporting Store

## Goal

Reduce repeated live Academic Online API traffic by synchronizing selected Academic Online faculty and publication data into a local SQL Server reporting store.

Academic Online remains the authoritative upstream system. The `academiconline.*` schema is a synchronized reporting cache with explicit retrieval timestamps and sync-run auditing.

## SQL objects

Run:

- `sql/411_V3E_PATCH019_AcademicOnlineLocalReportingStore_SQL2016.sql`
- `sql/412_V3E_PATCH019_AcademicOnlineLocalReportingStore_Diagnostics_SQL2016.sql`

The patch creates:

- `academiconline.SyncRun`
- `academiconline.Department`
- `academiconline.Scholar`
- `academiconline.ScholarDepartment`
- `academiconline.Publication`
- `academiconline.ScholarPublication`
- `academiconline.vw_DepartmentNewestPublication`

The source-specific schema is intentionally separate from `openalex.*`.

## Controlled department sync

Worker job name:

`AcademicOnlineDepartmentSync`

The job:

1. sends a department-filtered scholar search to Academic Online;
2. paginates scholar results;
3. retrieves each selected scholar's general profile;
4. retrieves publications only for the configured year window;
5. upserts scholars and publications into the local reporting store;
6. records scholar/department and scholar/publication relationships;
7. records run counts, status, timestamps, and failures in `academiconline.SyncRun`.

The job does not require a faculty member to exist in `openalex.Faculty`.

## Recommended first test

Set the Academic Online API key for the current PowerShell session:

```powershell
.\scripts\Set-AcademicOnlineApiKeyForCurrentSession.ps1
```

Then run one known Academic Online department with a deliberately small scholar cap:

```powershell
.\scripts\Run-AcademicOnlineDepartmentSync.ps1 `
    -DepartmentName "YOUR EXACT ACADEMIC ONLINE DEPARTMENT NAME" `
    -FromYear 2025 `
    -MaxScholars 10
```

For the first test, use the exact department text shown by the existing UVA Publication Lookup / Academic Online scholar profile.

After the worker completes, run the PATCH019 diagnostics script.

## Department report

Route:

`/Reports/DepartmentPublications`

The page reads SQL only. Searching or filtering the report does not call Academic Online.

The page supports:

- synchronized department selection;
- Top 25 / 50 / 100 / 250 / 500 newest publications;
- optional From Year filter;
- optional Verified-only filter;
- faculty attribution;
- deduplicated publication rows;
- DOI and OpenAlex identifiers;
- local cache freshness display.

## Publication deduplication

The local repository reuses an existing publication in this order:

1. Academic Online publication ID;
2. OpenAlex ID;
3. normalized DOI;
4. normalized title + publication year fallback.

A `ScholarPublication` bridge prevents one publication from needing to be stored once per faculty member.

## Current definition of "newest"

The currently parsed Academic Online publication model exposes publication year but not a confirmed full publication date.

PATCH019 therefore orders newest publications by:

1. publication year descending;
2. local publication key descending within a year.

If a reliable publication date is confirmed in Academic Online raw JSON later, the model and report should be upgraded to sort by the full source date.

## API-load strategy

PATCH019 intentionally does not perform a university-wide crawl automatically.

Recommended operating model:

- recent years: refresh more frequently;
- older publication history: refresh less frequently;
- manager reports: read SQL only;
- explicit department refresh: use the controlled worker job.

Scheduling should be added only after a successful controlled department sync and API-volume review.

## Provenance

Each scholar and publication keeps:

- source-specific identifiers;
- raw Academic Online JSON;
- first-seen timestamp;
- last-retrieved timestamp;
- last sync-run ID.

Each sync run records its department, year window, verified-only setting, counts, status, timestamps, summary, and errors.
