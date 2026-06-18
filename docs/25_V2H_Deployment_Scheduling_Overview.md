# Version 2H Deployment and Scheduling Overview

## What Changed

Version 2H adds:

- Admin job visibility
- Pipeline orchestration worker job
- Deployment config templates
- Local scripts
- Manager demo materials
- Readiness SQL

## Why the Web UI Does Not Directly Run Jobs

The package intentionally does not let an admin click a web button to mutate data in production. The web app shows the job commands and recent runs. The worker remains the controlled execution boundary.

This keeps production safer while still making the system understandable for demos and handoff.

## Pipeline Job

```text
RunPipelineOnce
```

Default sequence:

```text
InstitutionIngestion
AuthorIngestion
WorkIngestion
FacultyAuthorCandidateGeneration
FacultyPublicationCandidateGeneration
```

The pipeline stops on the first failure by default.
