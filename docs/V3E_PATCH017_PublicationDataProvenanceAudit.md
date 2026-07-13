# V3E PATCH017 - Publication Data Provenance and Responsibility Audit

## Purpose

PATCH017 prepares the application for future university discussions about who performs each part of the faculty-publication workflow.

It does not assign contractual ownership. It reports the source evidence already recorded by the application.

## Branch

```text
v3e-patch017-publication-data-provenance-audit
```

## New report

```text
/Reports/DataProvenance
```

The report separates four stages:

```text
1. Publication metadata supply
2. Faculty-author identity linking
3. Faculty-publication candidate matching
4. Final publication verification/decision
```

It also shows whether the same publication was observed in the latest available Academic Online snapshots and whether Academic Online marked it verified.

## Source interpretation

Typical recorded sources include:

```text
OpenAlex
AcceptedIdentityDecision
IdentityReviewBridge
AcceptedAuthorMatch
HumanReviewer
Academic Online observation
```

These values describe the application's recorded process. They do not prove institutional or vendor contractual responsibility.

## Added files

```text
src/OpenAlex.Data/Models/PublicationProvenanceDataRow.cs
src/OpenAlex.Data/PublicationProvenanceRepository.cs
src/OpenAlex.Web/Pages/Reports/DataProvenance.cshtml
src/OpenAlex.Web/Pages/Reports/DataProvenance.cshtml.cs
sql/360_V3E_PATCH017_PublicationDataProvenance_Diagnostics_SQL2016.sql
```

## Modified files

```text
src/OpenAlex.Data/OpenAlexDataServiceCollectionExtensions.cs
src/OpenAlex.Web/Pages/Shared/_Layout.cshtml
```

## Testing

```powershell
git fetch
git checkout v3e-patch017-publication-data-provenance-audit
git pull
dotnet build OpenAlexFacultyPublications.sln
```

Open:

```text
/Reports/DataProvenance
```

Run:

```sql
sql/360_V3E_PATCH017_PublicationDataProvenance_Diagnostics_SQL2016.sql
```

## Expected result

Each publication candidate should show:

```text
OpenAlex work metadata identifiers
identity candidate source
identity decision source
publication candidate source
publication decision source and reviewer
Academic Online observation and verified flag, when available
responsibility summary
```

Rows with missing source information or pending review are surfaced for future clarification.
