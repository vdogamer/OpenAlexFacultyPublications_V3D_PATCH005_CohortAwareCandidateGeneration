# V3E PATCH018 — Academic Online Faculty Publications HTML

## Goal

Provide a tangible manager demo and production path for:

1. resolving a faculty member to a durable, verified Academic Online scholar ID;
2. retrieving the scholar profile and **all** publication pages from Academic Online;
3. generating safe semantic HTML for the Research Faculty Directory / WordPress integration path.

The implementation uses the existing authenticated `AcademicOnlineApiClient`.

## Production improvement 1 — durable faculty mapping

PATCH018 creates:

`openalex.FacultyAcademicOnlineAuthorMap`

Key rules:

- only one current Academic Online mapping per faculty;
- only one current faculty per Academic Online author ID;
- prior mappings are retained when a mapping is replaced;
- mapping source, verification user, verification time, and evidence notes are retained.

Admin workflow:

`/Admin/AcademicOnlineMappings?FacultyId=16`

The admin can:

- inspect the current mapping;
- inspect the latest Academic Online comparison snapshot;
- promote the latest snapshot after review;
- explicitly replace the Academic Online author ID.

The production publication page **never chooses the first scholar search result**. It requires the durable current mapping.

The Academic Online comparison worker also prefers the durable current mapping when one exists. Scholar search remains only a discovery path for faculty who have no verified mapping yet.

## Production improvement 2 — complete pagination

`AcademicOnlineApiClient.GetAllAuthorPublicationsAsync(...)` loops publication pages until:

- Academic Online returns an empty page; or
- a page yields no new unique publications; or
- the configured `MaxPublicationPages` safeguard is reached.

This avoids assuming that Academic Online honors the requested page size.

The same pagination implementation is used by:

- the live HTML report;
- `AcademicOnlineComparisonJob`.

Configuration defaults:

```json
"AcademicOnline": {
  "BaseUrl": "https://profile.api.academic.online/virginia",
  "ApiKey": "",
  "TimeoutSeconds": 60,
  "PublicationRowsPerPage": 100,
  "MaxPublicationPages": 100
}
```

The API key is intentionally blank in source control.

## Manager demo

### 1. Database setup

Run:

- `sql/406_V3E_PATCH018_FacultyAcademicOnlineAuthorMap_SQL2016.sql`
- `sql/407_V3E_PATCH018_DemoFacultyAcademicOnlineMapping_SQL2016.sql`
- `sql/408_V3E_PATCH018_AcademicOnlineFacultyPublications_Diagnostics_SQL2016.sql`

The demo mapping script defaults to FacultyId 16 and promotes the latest previously captured Academic Online scholar snapshot.

### 2. Set Academic Online API key for the current PowerShell session

```powershell
.\scripts\Set-AcademicOnlineApiKeyForCurrentSession.ps1
```

Do not commit the key to `appsettings.json`.

### 3. Build

```powershell
dotnet clean OpenAlexFacultyPublications.sln
dotnet build OpenAlexFacultyPublications.sln
```

### 4. Launch Web

```powershell
dotnet run --project src/OpenAlex.Web/OpenAlex.Web.csproj
```

### 5. Open live manager demo

`/Reports/AcademicOnlineFacultyPublications?FacultyId=16`

The page shows:

- durable mapping used;
- live Academic Online scholar profile;
- number of API publication pages fetched;
- number of unique publications returned;
- rendered publication HTML;
- generated HTML source;
- standalone generated HTML link.

## HTML safety

External text is HTML-encoded before rendering. Publication URLs are limited to HTTP/HTTPS destinations, with DOI links normalized to `https://doi.org/`.

## Existing Faculty Directory context

The legacy Pubs / HTML page states that publication updates from Symplectic Elements ended August 20, 2024 and publication lists must be manually maintained until a replacement database is implemented. PATCH018 provides a concrete replacement-source path using Academic Online.

## Next integration slice after manager approval

Connect the generated HTML fragment/document to the desired downstream target:

- Research Faculty Directory Pubs / HTML storage;
- WordPress REST/content publishing;
- or a dynamic public faculty-profile route.
