# Version 2A Architecture Overview

## Main Goal

Create one maintainable application architecture for the OpenAlex faculty-publication workflow.

Version 1 proved the pieces. Version 2A organizes them.

## Target Workflow

```text
Faculty Source
→ Faculty Mirror
→ OpenAlex Institution / Author / Work Ingestion
→ Author Candidate Generation
→ Human Author Review
→ Publication Candidate Generation
→ Human Publication Review
→ Accepted Publication Reporting
→ CSV Export
→ Background Scheduling
→ Operational Monitoring
```

## Source of Truth Rules

1. Faculty identity comes from the internal faculty source.
2. OpenAlex is an enrichment source.
3. Candidate generation does not create truth.
4. Human review creates accepted truth.
5. Every accepted/rejected decision should be auditable.

## Integrated Project Boundaries

### OpenAlex.Web

The web app owns:

- Dashboard
- Faculty screens
- Author review screens
- Publication review screens
- Accepted publication reports
- CSV export UI
- Background job UI
- Operations dashboard
- Authentication/authorization boundary

### OpenAlex.Worker

The worker owns:

- Scheduled ingestion
- Scheduled candidate generation
- Scheduled exports
- Safe job execution
- Logging job events

### OpenAlex.Data

The data project owns:

- SQL Server connection factory
- Shared database options
- Low-level repository helpers
- Reader extension helpers

### OpenAlex.OpenAlexClient

The OpenAlex client owns:

- HTTP access to OpenAlex
- Institution/author/work endpoints
- Cursor paging
- Rate-friendly delays/retries later
- `mailto` configuration

### OpenAlex.Matching

The matching project owns:

- Scoring candidates
- Evidence summaries
- ORCID/OpenAlex ID matching
- Name/affiliation/title matching rules

### OpenAlex.Reporting

The reporting project owns:

- CSV exports
- Report row models
- Future Excel export support

### OpenAlex.OperationalLogging

The logging project owns:

- Shared application logging
- Error capture
- Health-check logging
- Operational events

## Future Production Shape

A production deployment may contain:

```text
IIS/Shibboleth
→ OpenAlex.Web
→ SQL Server

Windows Service / Scheduled Worker
→ OpenAlex.Worker
→ SQL Server
→ OpenAlex API
```
