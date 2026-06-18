# Version 3B Backlog — Persisted Scheduler + Safe Run Now

## Goal

Add a production-safe job scheduler model and controlled Admin “Run Now” workflow.

## Planned Work

- Persist integrated job definitions in SQL.
- Add job schedule table.
- Add job execution request table.
- Add Admin Run Now button with confirmation.
- Require Admin role for Run Now.
- Worker polls queued execution requests.
- Audit every requested job run.
- Enforce maximum load limits.
