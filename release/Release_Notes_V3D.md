# Release Notes — Version 3D

## Release Name

Background Service Deployment + Scheduled Execution Windows

## New

- `openalex.JobExecutionWindow`
- execution-window SQL scripts
- `ExecutionWindowRepository`
- `/Admin/ExecutionWindows`
- `/Admin/EditExecutionWindow`
- `ExecutionWindowOptions`
- Windows Service publish/install/uninstall scripts
- production worker service config template
- background-service deployment runbook
- Version 3E backlog

## Safety

Local defaults are permissive. Production can enforce approved execution windows before queued worker jobs run.
