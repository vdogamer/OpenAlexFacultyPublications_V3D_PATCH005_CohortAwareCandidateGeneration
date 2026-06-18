# Version 3D Background Service Runbook

## Local Smoke Test

1. Run SQL scripts 160-163.
2. Start web app.
3. Open `/Admin/ExecutionWindows`.
4. Confirm Local Demo Permissive Window exists.
5. Queue `InstitutionIngestionPreview`.
6. Run worker with `ProcessQueuedJobRequests`.
7. Confirm request completes.

## Production Setup

1. Publish worker.
2. Copy production worker appsettings.
3. Set real connection string.
4. Set `ExecutionWindows:EnforceExecutionWindows = true`.
5. Create/enable approved execution windows.
6. Install Windows Service.
7. Start service manually for first test.
8. Confirm `/Admin/JobRequests`.
9. Confirm `/Operations/Logs`.

## Safety

Do not enable FullLoad windows until approved.
