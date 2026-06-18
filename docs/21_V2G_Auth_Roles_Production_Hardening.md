# Version 2G Auth, Roles, and Production Hardening

## What Changed

Version 2G adds integrated web-app authentication and authorization.

## Auth Modes

### DemoCookie

Used for local testing.

```json
"Auth": {
  "Mode": "DemoCookie"
}
```

Users must exist in `openalex.AppUser`.

### ShibbolethHeaders

Used for production-style UVA/IIS/Shibboleth deployment.

```json
"Auth": {
  "Mode": "ShibbolethHeaders"
}
```

The app reads trusted server headers and maps them to users/roles.

## Policies

```text
ReaderPolicy   -> Reader, Reviewer, Admin
ReviewerPolicy -> Reviewer, Admin
AdminPolicy    -> Admin
```

## Protected Areas

```text
/Reports                ReaderPolicy
/Review                 ReviewerPolicy
/Operations             AdminPolicy
/Admin                  AdminPolicy
```

## Important Security Boundary

In Shibboleth mode, the app trusts the headers it receives. Therefore IIS/Shibboleth must strip or overwrite any incoming browser-supplied versions of these headers.

Do not expose this app directly to the public internet in Shibboleth-header mode without a trusted reverse proxy/IIS/Shibboleth boundary.
