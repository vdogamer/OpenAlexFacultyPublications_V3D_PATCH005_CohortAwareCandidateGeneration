# Release Notes — Version 2I

## Release Name

Final Production Polish + Operational Dashboard Integration

## Release Type

Final integrated production-polish release.

## New

- Operational dashboard snapshot
- Operational metric cards
- `/Operations/Health`
- `/Operations/Errors`
- `/Operations/Logs`
- `/Operations/Runs`
- `/Operations/Requests`
- Generic operational table reader
- Production readiness SQL
- Operational dashboard audit SQL
- Final smoke-test SQL
- Smoke-test PowerShell helper
- Final manager demo script
- Manager signoff checklist
- Version 3 backlog

## Safety

Operational pages are Admin-only.

Data-changing jobs still run through the worker process, not the web UI.
