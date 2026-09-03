# V3E PATCH020B — Durable Worker Hosting + Startup/Recovery

## Goal

Run the Academic Online department refresh scheduler as a durable Windows background service instead of a PowerShell process tied to an interactive login session.

PATCH020B builds directly on PATCH020/PATCH020A:

- PATCH020 decides which departments are due and leases work safely;
- PATCH020A records scheduler heartbeat and pass health;
- PATCH020B makes that scheduler survive sign-out, PowerShell closure, reboot, and ordinary worker-process crashes.

No new database migration is required for PATCH020B.

## Worker host

The existing `OpenAlex.Worker` remains a .NET `BackgroundService`.

PATCH020B adds `Microsoft.Extensions.Hosting.WindowsServices` and registers Windows Service lifetime support.

The worker still runs normally from `dotnet run` during development. Windows Service behavior activates only when the published executable is started by the Windows Service Control Manager.

## Installation

Run PowerShell as Administrator from the repository root.

For the current local SQL Server configuration, the database connection uses `Trusted_Connection=True`.

Therefore, for local validation, install the service using the same Windows account that already has access to `OpenAlexIngestion_Test`.

First set the Academic Online API key for the current administrator PowerShell session:

```powershell
.\scripts\Set-AcademicOnlineApiKeyForCurrentSession.ps1
```

Then install:

```powershell
.\scripts\Install-AcademicOnlineDepartmentRefreshService.ps1 `
    -ServiceIdentity CurrentUser `
    -PollSeconds 900 `
    -MaxDepartmentsPerPass 1 `
    -LeaseMinutes 180 `
    -StartAfterInstall
```

The installer will separately prompt for the Windows service logon credential.

The published worker is placed under:

`C:\ProgramData\OpenAlex\AcademicOnlineDepartmentRefresh`

by default.

## API-key handling

The Academic Online API key is not committed to GitHub and is not placed on the Windows service command line.

The installer stores it in the service-specific environment block under the service's HKLM registry configuration.

This value is readable by administrators, so production secret-management requirements should be reviewed before deploying beyond the local/internal Windows host.

## Service identity

Two installer modes are supported:

### CurrentUser

Recommended for the current local validation because SQL uses Windows integrated authentication.

The installer prompts for the Windows account password and creates the service using that credential.

### LocalSystem

Useful only when the target SQL Server and other dependencies explicitly permit the LocalSystem service identity.

Do not assume that LocalSystem automatically has database access.

## Startup

The service is configured as:

- Automatic;
- delayed automatic start.

Delayed startup gives SQL Server and network dependencies time to settle after Windows boots.

## Recovery

Windows Service Control Manager is configured to restart the service after process-level failure:

1. first failure: restart after 60 seconds;
2. second failure: restart after 60 seconds;
3. subsequent failure: restart after 300 seconds;
4. failure count resets after 24 hours.

PATCH020A remains the application-level health signal. A Windows service can be `Running` while its application heartbeat is stale, so both layers matter.

## Service configuration

The service-specific environment configures:

- `Worker:Enabled = true`
- `Worker:JobName = AcademicOnlineDepartmentRefreshScheduler`
- `Worker:RunOnceAndStop = false`
- normal polling interval;
- scheduler key;
- maximum departments per pass;
- lease duration;
- PATCH020B operational-logging identity;
- Academic Online API key.

If no explicit connection string is supplied to the installer, the published worker uses its packaged `appsettings.json`.

A production install should supply an explicit production connection string rather than relying on the local test configuration.

## Optional explicit connection string

Example:

```powershell
.\scripts\Install-AcademicOnlineDepartmentRefreshService.ps1 `
    -ServiceIdentity CurrentUser `
    -ConnectionString "Server=...;Database=...;Trusted_Connection=True;TrustServerCertificate=True;" `
    -PollSeconds 900 `
    -StartAfterInstall
```

The connection string is stored in the same service-specific environment block. It is not placed on the service command line.

## Inspect service state

Run:

```powershell
.\scripts\Get-AcademicOnlineDepartmentRefreshServiceStatus.ps1
```

This reports:

- Windows service status;
- startup type;
- delayed-start state;
- process ID;
- service account;
- executable path;
- Windows recovery policy.

It intentionally does not print the service environment block because that contains the Academic Online API key.

## Start / stop / restart

```powershell
Start-Service OpenAlexAcademicOnlineDepartmentRefresh

Stop-Service OpenAlexAcademicOnlineDepartmentRefresh

Restart-Service OpenAlexAcademicOnlineDepartmentRefresh
```

A deliberate `Stop-Service` does not represent a crash and should not be used to test automatic failure recovery.

## Application health validation

After starting the service, wait slightly longer than one poll interval and run:

`sql/420_V3E_PATCH020A_SchedulerHeartbeat_Diagnostics_SQL2016.sql`

or open:

`/Admin/AcademicOnlineDepartments`

Expected:

- Windows service: `Running`
- scheduler mode: continuous;
- PATCH020A health: `ACTIVE`
- heartbeat continues advancing while no user is logged in;
- if no department is due, zero departments are claimed and no Academic Online refresh occurs.

For a faster initial validation, install with `-PollSeconds 60`, verify two or three advancing heartbeats, then reinstall with the normal `900` second poll.

## Recovery validation

After basic service/heartbeat validation succeeds, an administrator may test SCM recovery by forcibly terminating the service process rather than issuing `Stop-Service`.

Use the status helper to obtain the process ID.

Then:

```powershell
Stop-Process -Id <PID> -Force
```

Expected:

- service temporarily stops;
- SCM restarts it according to the configured recovery policy;
- PATCH020A heartbeat resumes after restart.

Do this only during a controlled test window.

## Reboot validation

The final durable-hosting proof is:

1. service is Running;
2. reboot Windows;
3. do not manually launch the worker;
4. after delayed startup + one poll interval, inspect the service and PATCH020A heartbeat;
5. confirm the service is Running and heartbeat is ACTIVE.

No Academic Online API call occurs merely because Windows rebooted unless a department is actually due.

## Uninstall

Run PowerShell as Administrator:

```powershell
.\scripts\Uninstall-AcademicOnlineDepartmentRefreshService.ps1
```

To remove the published binaries too:

```powershell
.\scripts\Uninstall-AcademicOnlineDepartmentRefreshService.ps1 `
    -RemovePublishedFiles
```

Deleting the Windows service removes its service-specific registry configuration, including the API-key environment value.

## Production caution

PATCH020B provides durable Windows hosting, but it does not decide the enterprise deployment topology.

Before deploying to a shared server, confirm:

- intended Windows service account;
- SQL Server permissions;
- production connection string;
- organizational secret-management policy;
- firewall/network access to Academic Online;
- service monitoring / alerting ownership.

The scheduler itself remains deliberately conservative: only enabled, due departments are allowed to call Academic Online.
