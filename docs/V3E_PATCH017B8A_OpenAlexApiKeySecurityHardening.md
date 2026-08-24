# V3E PATCH017B8A - OpenAlex API Key Security Hardening

## Purpose

PATCH017B8A removes the OpenAlex API credential from tracked configuration and prevents query-string credentials from being emitted or persisted in normal application logging.

## Changes

- `src/OpenAlex.Worker/appsettings.json`: tracked `OpenAlex:ApiKey` value is blank.
- `src/OpenAlex.Web/appsettings.json`: tracked `OpenAlex:ApiKey` value is blank.
- Worker and Web suppress informational `HttpClient` URI logging for `OpenAlexApiClient`.
- `OpenAlexRequestUriRedactor` redacts `api_key` and `mailto` query values.
- Institution, Author, and Work ingestion persist only redacted request URLs.
- OpenAlex response models expose redacted request URLs to callers.
- `sql/389...` replaces historical raw `RequestLog.RequestUrl` values with the already-masked `RequestUrlMasked` value.
- `sql/390...` verifies that known request-log locations no longer contain unredacted query values.
- `scripts/Set-OpenAlexApiKeyForCurrentSession.ps1` prompts for the rotated key without echoing it and stores it only in the current PowerShell process as `OpenAlex__ApiKey`.

## Required incident response

The previously configured key must be treated as exposed because it appeared in tracked configuration and console output. Revoke/rotate that key before additional OpenAlex API work. Removing it from the latest Git commit does not invalidate copies in earlier Git history or saved logs.

Do not paste the replacement key into source files, command-line arguments, chat messages, screenshots, tickets, or documentation.

## Local runtime setup

After rotating the key, from the repository root run:

```powershell
.\scripts\Set-OpenAlexApiKeyForCurrentSession.ps1
```

Paste the replacement key only at the secure prompt. The script sets:

```text
OpenAlex__ApiKey
```

for the current PowerShell process. .NET configuration maps that environment variable to `OpenAlex:ApiKey`, overriding the blank tracked `appsettings.json` value.

The environment variable disappears when that PowerShell process closes.

## Database cleanup

Run:

```text
sql/389_V3E_PATCH017B8A_OpenAlexRequestLogSecretScrub_SQL2016.sql
```

Then verify with:

```text
sql/390_V3E_PATCH017B8A_OpenAlexSecretHardening_Diagnostics_SQL2016.sql
```

Expected security result:

```text
RawApiKeyRowsInRequestUrl = 0
RawMailToRowsInRequestUrl = 0
RawApiKeyRowsInRequestUrlMasked = 0
SecurityStatus = DatabaseRequestUrlsRedacted
```

## Build verification

```powershell
dotnet clean OpenAlexFacultyPublications.sln
dotnet build OpenAlexFacultyPublications.sln
```

Expected: build succeeds with zero errors.

## Console verification

For the next OpenAlex worker job, normal informational output should no longer print `System.Net.Http.HttpClient.OpenAlexApiClient` request lines containing the full request URI. Application-level job progress and warnings/errors remain available.

## Git-history note

The replacement key is never committed. The old exposed key may remain in historical commits; rotation is therefore mandatory. History rewriting can be considered separately if repository policy requires removal of dead secrets, but it should not delay revocation/rotation.
