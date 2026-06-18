# Release Notes — Version 2A

## Release Name

Integrated Application Skeleton

## Type

Architecture consolidation package.

## New Projects

- OpenAlex.Web
- OpenAlex.Worker
- OpenAlex.Data
- OpenAlex.OpenAlexClient
- OpenAlex.Matching
- OpenAlex.Reporting
- OpenAlex.OperationalLogging

## New SQL

- `040_V2A_Integrated_Readiness_Check.sql`
- `041_V2A_Component_Register.sql`
- `042_V2A_Post_Integration_Demo_Queries.sql`

## Breaking Changes

None. Version 2A does not modify existing V1 tables except for optional creation/upsert of `openalex.ApplicationComponent`.

## Next Version

Version 2B: migrate real V1 logic into the integrated solution.
