# Release Notes — Version 3B

## Release Name

Persisted Scheduler + Safe Admin Run Now

## New

- Scheduler SQL tables
- Seeded job definitions
- SchedulerRepository
- `/Admin/Scheduler`
- `/Admin/RunJob`
- `/Admin/JobRequests`
- QueuedJobRequestProcessorJob
- Worker job route: `ProcessQueuedJobRequests`
- Scheduler readiness/audit SQL
- Scheduler runbook
- Version 3C role-management backlog

## Safety

The web UI queues job requests only. The worker executes queued jobs. All queued requests and execution events are persisted.
