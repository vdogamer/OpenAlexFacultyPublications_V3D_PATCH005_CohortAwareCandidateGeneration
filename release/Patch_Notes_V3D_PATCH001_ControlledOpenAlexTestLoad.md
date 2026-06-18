# Patch Notes — V3D PATCH001 Controlled OpenAlex TestLoad Readiness

## Why this patch exists

OpenAlex changed API access behavior. The previous `mailto` polite-pool pattern has been replaced by API keys.

## Changes

- Added `OpenAlex:ApiKey` option.
- Appends `api_key` to OpenAlex API requests.
- Keeps `mailto` as optional backward-compatible contact metadata.
- Masks `api_key` and `mailto` in request logs.
- Updates configuration preflight checks.
- Adds controlled TestLoad milestone documentation.
- Adds TestLoad preflight and after-action SQL scripts.

## New Files

```text
docs/45_Controlled_OpenAlex_TestLoad_Milestone.md
sql/170_TestLoad_Preflight_OpenAlex.sql
sql/171_TestLoad_After_Action_Report.sql
scripts/run-controlled-testload-sequence.ps1
```
