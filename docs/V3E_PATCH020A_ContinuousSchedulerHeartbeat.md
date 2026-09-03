# V3E PATCH020A — Continuous Scheduler Heartbeat + Operational Status

## Goal

Make the PATCH020 background scheduler observable before it is left running continuously.

PATCH020A records every scheduler pass, including passes where no departments are due, and exposes the scheduler state on the Department Catalog admin page.

## Database migration

After PATCH020, run:

`sql/419_V3E_PATCH020A_SchedulerHeartbeat_SQL2016.sql`

Then validate with:

`sql/420_V3E_PATCH020A_SchedulerHeartbeat_Diagnostics_SQL2016.sql`

The migration creates:

`academiconline.RefreshSchedulerState`

## Recorded state

For the Academic Online department refresh scheduler, PATCH020A records:

- scheduler key;
- last heartbeat time;
- last pass start and completion;
- pass status;
- worker/lease owner;
- configured polling interval;
- one-pass versus continuous mode;
- departments claimed;
- departments completed;
- departments partial;
- departments failed;
- last scheduler-level exception.

A scheduler pass with no due departments still records a successful heartbeat.

## Admin visibility

Route:

`/Admin/AcademicOnlineDepartments`

The page shows one of:

- `NOT STARTED` — no heartbeat has ever been recorded;
- `ONE-PASS` — the most recent scheduler process was intentionally run once;
- `ACTIVE` — continuous mode is configured and the heartbeat is fresh;
- `STALE` — continuous mode was configured, but the heartbeat is older than the expected polling window.

For continuous mode, the healthy window is:

`(2 × PollIntervalSeconds) + 60 seconds`

This allows normal scheduling jitter without hiding a stopped worker for too long.

## Recommended validation without API traffic

If no department is currently due, run one scheduler pass:

```powershell
.\scripts\Set-AcademicOnlineApiKeyForCurrentSession.ps1

.\scripts\Run-AcademicOnlineDepartmentRefreshScheduler.ps1 `
    -MaxDepartments 1
```

Because no department is due, the pass should make no Academic Online calls.

Then run SQL 420. Expected shape:

- `HeartbeatHealth = ONE-PASS`
- `DepartmentsClaimed = 0`
- `DepartmentsCompleted = 0`
- `DepartmentsPartial = 0`
- `DepartmentsFailed = 0`
- `LastPassStatus = Completed`

## Continuous-mode validation

For a short local test:

```powershell
.\scripts\Run-AcademicOnlineDepartmentRefreshScheduler.ps1 `
    -MaxDepartments 1 `
    -Continuous `
    -DelaySeconds 60
```

Leave the worker running for at least two polling intervals.

While it is running, SQL 420 and the admin page should show:

- `IsContinuous = 1`
- `HeartbeatHealth = ACTIVE`
- heartbeat timestamps advancing about once per minute;
- zero claims when no department is due.

Stop the local test with Ctrl+C.

For the normal operating cadence, use a longer polling interval such as 900 seconds (15 minutes). A no-due pass does not call Academic Online.

## Scheduler pass status

`Completed` means the scheduler infrastructure pass itself completed and all claimed departments completed successfully.

`CompletedWithIssues` means the pass completed but one or more claimed departments ended partial or failed.

`Failed` means the scheduler pass itself threw an exception.

Department-level failures continue to preserve PATCH019B's last known-good complete manager snapshot.

## Important scope note

Scheduler heartbeat health is separate from department publication scope.

For example, a department configured with `RefreshVerifiedOnly = 1` will continue producing verified-only snapshots even when the scheduler itself is healthy. Change department policy independently in the Department Catalog if all publications are desired.
