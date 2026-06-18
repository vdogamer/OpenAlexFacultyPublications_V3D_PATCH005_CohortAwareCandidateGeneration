# Version 3B Persisted Scheduler + Safe Run Now

## What Changed

Version 3B adds a database-backed job scheduler and safe run request workflow.

## Tables

```text
openalex.JobDefinition
openalex.JobExecutionRequest
openalex.JobExecutionEvent
```

## Workflow

1. Admin opens `/Admin/Scheduler`.
2. Admin chooses a job.
3. Admin confirms the job on `/Admin/RunJob`.
4. Web inserts a `Queued` request.
5. Worker runs `ProcessQueuedJobRequests`.
6. Worker claims one queued request.
7. Worker executes the job.
8. Worker marks request `Completed` or `Failed`.
9. Admin reviews `/Admin/JobRequests`.

## Why This Is Safer

The web process never directly runs write jobs. It only queues requests. The worker remains the controlled execution boundary.
