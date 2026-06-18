# Release Notes — Version 2H

## Release Name

Deployment, Scheduling, and Manager Handoff

## Release Type

Integrated deployment/handoff release.

## New

- `RunPipelineOnce` worker job
- `PipelineOrchestrationOptions`
- `PipelineOrchestrationJob`
- Job dashboard repository
- Admin jobs page
- Admin deployment page
- Local PowerShell helper scripts
- Production appsettings templates
- IIS web.config template
- Deployment checklist
- Manager demo runbook
- V2H readiness/audit SQL scripts
- V2I backlog

## Safety

The web UI does not directly execute write jobs. Data-changing jobs still run through the worker process.

The worker remains disabled by default.
