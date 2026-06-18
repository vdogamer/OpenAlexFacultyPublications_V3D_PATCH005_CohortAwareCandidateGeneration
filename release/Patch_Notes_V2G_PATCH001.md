# Patch Notes — Version 2G PATCH001

## Fix

Patched `sql/100_V2G_Auth_Role_Readiness_Check.sql`.

## Problem

The original readiness script selected optional columns from `openalex.AppRole`:

- `RoleDescription`
- `IsSystemRole`

The actual V1N auth schema in the user's database does not include those columns, causing:

```text
Msg 207 Invalid column name 'RoleDescription'
Msg 207 Invalid column name 'IsSystemRole'
```

## Resolution

The readiness script now uses schema-safe queries and avoids assuming optional role metadata columns.
