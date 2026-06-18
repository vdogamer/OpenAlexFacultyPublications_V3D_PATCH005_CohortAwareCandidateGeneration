# Patch Notes — Version 2B PATCH002

## Fix

Corrected database connection-string handling.

## Problem

The V2B package appsettings used:

```json
"Database": {
  "ConnectionString": "..."
}
```

But `SqlConnectionFactory` was still looking only for:

```json
"ConnectionStrings": {
  "DefaultConnection": "..."
}
```

That caused pages that touched the database to throw a runtime exception and show the generic error page.

## Resolution

`SqlConnectionFactory` now supports both:

- `Database:ConnectionString`
- `ConnectionStrings:DefaultConnection`

## Also Included

- Updated appsettings examples.
- Added `docs/08_Runtime_Troubleshooting.md`.
