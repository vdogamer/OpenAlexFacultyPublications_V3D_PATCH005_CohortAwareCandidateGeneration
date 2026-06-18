# Migration From V1 Packages to Version 2A

## Purpose

This guide shows where each Version 1 component should move inside the integrated Version 2A solution.

## Migration Map

| V1 Package | Move Into | Notes |
|---|---|---|
| V1A Institution ingestion | `OpenAlex.OpenAlexClient` + `OpenAlex.Worker` | Client logic goes to client library; job orchestration goes to worker |
| V1B Author ingestion | `OpenAlex.OpenAlexClient` + `OpenAlex.Worker` | Use shared paging/client code |
| V1C Work ingestion | `OpenAlex.OpenAlexClient` + `OpenAlex.Worker` | Keep work upsert logic in worker/data service |
| V1D Faculty author candidates | `OpenAlex.Matching` + `OpenAlex.Worker` | Scoring logic belongs in matching library |
| V1E Human review decisions | `OpenAlex.Web` | Review pages/actions belong in web app |
| V1F Publication candidates | `OpenAlex.Matching` + `OpenAlex.Worker` | Evidence and scoring logic belongs in matching library |
| V1G Publication review decisions | `OpenAlex.Web` | Review pages/actions belong in web app |
| V1H Reporting/CSV export | `OpenAlex.Reporting` + `OpenAlex.Web` | Export service plus UI button |
| V1K Web UI hardening | `OpenAlex.Web` | Layout, filters, role checks |
| V1L Faculty source integration | `OpenAlex.Web` + future `OpenAlex.FacultySource` | Faculty sync may become its own library later |
| V1M Background jobs | `OpenAlex.Worker` + `OpenAlex.Web` | Worker executes; web displays/administers |
| V1N Auth/roles | `OpenAlex.Web` | Shibboleth-ready auth boundary |
| V1O Operational logging | `OpenAlex.OperationalLogging` + `OpenAlex.Web` | Shared logger plus dashboard |

## Recommended Migration Order

### Step 1 — Shared SQL/Data

Move reusable SQL connection and reader helpers into `OpenAlex.Data`.

### Step 2 — Operational Logging

Move Version 1O logging service into `OpenAlex.OperationalLogging`. Then every app can log the same way.

### Step 3 — OpenAlex Client

Move the HTTP/cursor paging code from V1A–V1C into `OpenAlex.OpenAlexClient`.

### Step 4 — Worker Jobs

Move console-job logic into `OpenAlex.Worker` job classes.

### Step 5 — Web UI

Move the strongest Razor Pages from V1K/V1L/V1N/V1O into `OpenAlex.Web`.

### Step 6 — Matching

Move V1D/V1F scoring logic into `OpenAlex.Matching`.

### Step 7 — Reporting

Move V1H export logic into `OpenAlex.Reporting`.

## Rule

Do not rewrite working logic unless needed. Move working logic into the correct project, then improve it.
