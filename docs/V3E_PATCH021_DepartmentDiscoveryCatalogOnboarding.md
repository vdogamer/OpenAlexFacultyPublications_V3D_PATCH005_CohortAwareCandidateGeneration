# V3E PATCH021 — Academic Online Department Discovery + Catalog Onboarding

## Goal

Discover UVA School of Medicine organizational units from Academic Online and onboard them into the local department catalog without manually identifying a faculty member first.

PATCH021 preserves the operating rules established by PATCH020 through PATCH020B:

- web requests do not perform Academic Online synchronization;
- the durable Windows Service performs external API work;
- newly discovered departments are disabled by default;
- an administrator reviews and explicitly enables refresh policy;
- manager-facing publication reports remain SQL-backed.

## Database migration

After PATCH020B, run:

`sql/421_V3E_PATCH021_DepartmentDiscovery_SQL2016.sql`

Validate with:

`sql/422_V3E_PATCH021_DepartmentDiscovery_Diagnostics_SQL2016.sql`

PATCH021 adds Academic Online identity/discovery metadata to `academiconline.Department` and creates:

- `academiconline.DepartmentDiscoveryRequest`
- `academiconline.DepartmentDiscoveryRun`

## Discovery strategy

Academic Online's existing scholar-search endpoint is scanned in bounded pages with no faculty-name or department filter.

For each unique scholar returned, PATCH021 parses structured organizational affiliations:

- organization ID;
- name;
- slug;
- parent ID;
- parent name;
- parent slug.

For a requested parent such as `School of Medicine`, PATCH021 keeps direct child organizational units whose structured parent metadata resolves to that parent.

Discovered units are deduplicated by:

1. Academic Online department ID;
2. slug;
3. display name fallback.

The number of unique observed scholars associated with each unit is retained as discovery evidence.

## Safeguards

Discovery is explicitly bounded by:

- rows per page: 100 by default;
- maximum pages: administrator-configured, default 100.

If the page safeguard is reached:

- observed departments are still upserted;
- the discovery run is marked `Partial`;
- the UI warns that the catalog may not be exhaustive.

If Academic Online returns zero scholars or zero direct child units, the run is marked `Failed` rather than silently creating an empty catalog.

## Queue and durable scheduler

Admin route:

`/Admin/AcademicOnlineDepartments`

The **Discover Departments** card queues a request only.

The installed durable scheduler checks for at most one pending discovery request on each normal scheduler pass, claims it with a lease, runs discovery, records the audit run, and then continues processing ordinary due-department refreshes.

If the worker crashes while a discovery request is running, the request can be reclaimed after its lease expires.

## Catalog onboarding

Newly discovered departments are inserted with:

- `IsActive = 1`;
- `RefreshEnabled = 0`;
- normal interval = 168 hours;
- retry interval = 60 minutes;
- default publication scope = previous calendar year through present;
- verified-only = false;
- max scholars = 250.

For 2026, the default From Year is therefore 2025.

Existing departments such as Department of Cell Biology are matched by Academic Online ID, slug, or exact source name and enriched rather than duplicated.

The admin page displays:

- Academic Online ID/slug;
- parent organization;
- observed scholar count;
- discovery last-seen time;
- ordinary scheduling/snapshot controls.

## Deploy updated worker binaries

PATCH021 changes the installed Windows Service worker code.

After pulling/building the patch, update the already-installed service without changing its account, API key, startup policy, recovery policy, or service-specific environment:

```powershell
.\scripts\Update-AcademicOnlineDepartmentRefreshServiceBinaries.ps1
```

Run this from an Administrator PowerShell.

## First validation

1. Pull PATCH021 and build.
2. Run SQL 421.
3. Update the durable service binaries.
4. Start/restart the web application.
5. Open:

   `/Admin/AcademicOnlineDepartments`

6. In **Discover Departments**, leave:

   - Parent organization: `School of Medicine`
   - Maximum scholar pages: `100`

7. Click **Queue Department Discovery**.

Because the durable service normally polls every 900 seconds, the request may wait up to 15 minutes.

For an immediate validation pass, restart the service once:

```powershell
Restart-Service OpenAlexAcademicOnlineDepartmentRefresh
```

The worker executes a scheduler pass immediately on service startup.

8. After the pass completes, run SQL 422.

## Expected successful result

The latest request should show:

- `RequestStatus = Completed` or `Partial`;
- a non-null `DiscoveryRunId`;
- `ScholarsScanned > 0`;
- `DepartmentsDiscovered > 0`.

A fully bounded scan should ideally show:

- `RunStatus = Completed`;
- `HitMaxPages = 0`.

The department catalog should contain multiple School of Medicine child units, all newly inserted units with `RefreshEnabled = 0`.

Department of Cell Biology should be updated with Academic Online identity metadata rather than duplicated.

## If discovery fails

Run SQL 422 first.

The request/run ErrorText is the primary diagnostic.

Also inspect:

```sql
SELECT TOP (20)
    CreatedAtUtc,
    LogLevel,
    Category,
    EventName,
    Message,
    ExceptionType,
    ExceptionMessage
FROM openalex.OperationalLog
WHERE Category IN
(
    N'AcademicOnlineDepartmentDiscovery',
    N'Worker'
)
ORDER BY CreatedAtUtc DESC;
```

The most important first validation is whether Academic Online permits an unfiltered paged scholar search. PATCH021 deliberately fails visibly if it does not, so we can adapt the discovery strategy without contaminating the catalog.

## API-load note

A 15-minute scheduler heartbeat does not repeatedly run discovery.

Discovery occurs only when an administrator explicitly queues a discovery request.

Ordinary no-due scheduler passes continue making no Academic Online synchronization calls.
