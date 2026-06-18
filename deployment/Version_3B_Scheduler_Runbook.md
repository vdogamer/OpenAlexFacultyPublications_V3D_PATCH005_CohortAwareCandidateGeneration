# Version 3B Scheduler Runbook

## Safe Operational Model

Admins queue jobs. Workers execute jobs.

Do not enable direct data-changing job execution inside the web process until the team approves that risk.

## Normal Test

1. Queue `InstitutionIngestionPreview`.
2. Process one queued request.
3. Confirm status.
4. Queue `InstitutionIngestion` only with small limits.
5. Confirm ingestion run and request logs.

## Production Recommendation

Keep:

```json
"QueuedJobProcessor": {
  "MaxRequestsPerRun": 1,
  "StopOnFirstFailure": true
}
```
