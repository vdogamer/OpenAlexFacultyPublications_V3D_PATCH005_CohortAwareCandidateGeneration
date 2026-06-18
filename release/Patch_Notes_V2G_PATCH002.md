# Patch Notes — Version 2G PATCH002

## Fixes

Patched:

- `sql/101_V2G_Auth_Audit_Queries.sql`
- `src/OpenAlex.Data/AuthRepository.cs`

## Problem

The V2G package assumed these optional `openalex.AppUser` columns existed:

- `ComputingId`
- `LastLoginAtUtc`

The actual V1N auth schema in the user's database does not include those columns.

## Resolution

The audit SQL now avoids those optional columns.

`AuthRepository` now checks whether those columns exist before referencing them. This prevents login/admin pages from failing against the leaner V1N auth schema.
