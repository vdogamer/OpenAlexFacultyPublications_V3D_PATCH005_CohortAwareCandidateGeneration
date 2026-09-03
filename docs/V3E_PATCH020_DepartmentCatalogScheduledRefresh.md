# V3E PATCH020 — Department Catalog + Controlled Scheduled Refresh

## Goal

Operate the Academic Online department cache without manually editing environment variables for every department refresh.

PATCH020 adds:

- an administrator-facing department catalog;
- per-department refresh policy;
- due / waiting / disabled state;
- safe "mark due now" behavior;
- leased refresh claims to prevent overlapping workers;
- normal refresh cadence after complete runs;
- shorter retry cadence after partial or failed runs;
- a worker mode that processes only enabled departments that are due.

The manager-facing department publication report remains SQL-backed.

## Database migration

After PATCH019B, run:

`sql/417_V3E_PATCH020_DepartmentCatalogScheduledRefresh_SQL2016.sql`

Then validate with:

`sql/418_V3E_PATCH020_DepartmentCatalogScheduledRefresh_Diagnostics_SQL2016.sql`

All existing departments are disabled by default.

For existing promoted snapshots, PATCH020 seeds refresh scope from the current snapshot where possible. It also seeds the next due timestamp using the default 168-hour interval.

## Department catalog

Admin route:

`/Admin/AcademicOnlineDepartments`

Each department exposes:

- enable / disable scheduled refresh;
- normal refresh interval in hours;
- retry interval in minutes;
- From Year / To Year scope;
- Verified-only setting;
- maximum scholars;
- current promoted snapshot;
- next due time;
- latest scheduled attempt status;
- lease information when a worker has claimed the department;
- last error.

The web page never calls Academic Online.

"Mark Due Now" only changes `NextRefreshDueAtUtc`. The background worker still performs the API work.

## Safe scheduler behavior

Worker job name:

`AcademicOnlineDepartmentRefreshScheduler`

Each scheduler pass:

1. atomically claims at most the configured number of due departments;
2. assigns a time-limited lease;
3. invokes the existing PATCH019B department sync path;
4. preserves PATCH019B promotion rules;
5. on a complete refresh, schedules the next normal interval;
6. on a partial or failed refresh, schedules the shorter retry interval;
7. releases the lease.

Only departments with:

- `IsActive = 1`
- `RefreshEnabled = 1`
- due time reached
- no active lease

may be claimed.

## Recommended first validation

1. Apply SQL 417.
2. Run SQL 418.
3. Open `/Admin/AcademicOnlineDepartments`.
4. For **Department of Cell Biology**, configure:

   - Enable scheduled refresh: Yes
   - Normal interval: 168 hours
   - Retry: 60 minutes
   - From year: 2025
   - To year: blank
   - Verified only: No
   - Maximum scholars: 25

5. Save the policy.
6. Click **Mark Due Now**.
7. Set the Academic Online API key in the current PowerShell session.
8. Run one scheduler pass:

```powershell
.\scripts\Set-AcademicOnlineApiKeyForCurrentSession.ps1

.\scripts\Run-AcademicOnlineDepartmentRefreshScheduler.ps1 `
    -MaxDepartments 1
```

9. Re-run SQL 418.

Expected result:

- Cell Biology is no longer due;
- `LastRefreshAttemptStatus = Completed` for a complete run;
- `LastRefreshAttemptRunId` points to the new Academic Online sync run;
- `NextRefreshDueAtUtc` is approximately 168 hours after completion;
- the new complete run becomes the promoted current snapshot.

## Partial / failed behavior

If the department refresh is partial:

- the PATCH019B current snapshot remains unchanged;
- scheduler attempt status becomes `Partial`;
- the department becomes due again after `RefreshFailureRetryMinutes`.

If the refresh fails:

- the current snapshot remains unchanged;
- attempt status becomes `Failed`;
- the error is retained;
- the department becomes due again after the failure-retry interval.

## Continuous mode

After one-pass validation, the same worker can poll continuously:

```powershell
.\scripts\Run-AcademicOnlineDepartmentRefreshScheduler.ps1 `
    -MaxDepartments 1 `
    -Continuous `
    -DelaySeconds 900
```

This checks for due departments every 15 minutes.

A polling pass with no due departments makes no Academic Online calls.

## Operational caution

PATCH020 does not automatically enable every department. Add departments deliberately and enable refresh only after validating their Academic Online identity and scope.

The default scheduler concurrency is intentionally conservative: one department per pass.
