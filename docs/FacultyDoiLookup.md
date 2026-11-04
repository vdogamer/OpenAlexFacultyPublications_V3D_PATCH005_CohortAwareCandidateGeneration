# Faculty DOI lookup

Open **Faculty DOI Lookup** in the navigation, or visit
`/Reports/FacultyDois`. Enter a UVA userid/ComputingID (for example, `cf2s`)
or its `@virginia.edu` email address, then select **Find DOIs**.

The report returns one clickable entry per normalized DOI, with publication
title, year, and source. Results are ordered newest first. There is no result
cap or additional year/verification filter. Bare identifiers, `doi:` identifiers,
and DOI resolver URLs are deduplicated without regard to capitalization.
Blank or malformed DOI values are omitted.

## Data and access

The page uses the app's existing database connection and `ReaderPolicy`
(Reader, Reviewer, or Admin). It makes no external API requests and performs no
database writes. No SQL migration is needed on the current release schema.

Exact identity matches use:

- `academiconline.Scholar.UvaUserId` or the scholar's UVA email;
- active `academiconline.DepartmentRosterMember.ExpectedEmail`;
- `openalex.Faculty.PrimaryEmail` or `SourceFacultyKey`, where that column exists.

Names are displayed as confirmation and are never used as lookup keys.

Synchronized publications use current scholar or roster publication relationships
and `academiconline.vw_DepartmentNewestPublication`. This preserves the existing
promoted department snapshot and year-policy boundary, including mixed
Academic Online/OpenAlex roster sources. Historical and incomplete snapshots
are not promoted or exposed by the new page. Native OpenAlex records include
current accepted publication decisions only.

The report returns all distinct DOI-bearing publications in those available
sources. It does not claim to provide a faculty member's entire publishing
history beyond the collected and approved coverage. A missing faculty record,
a matched faculty member with no valid DOI, and a database failure have distinct
UI states. Missing optional sources are skipped; if no supported source exists,
the request fails visibly instead of reporting no faculty match.

## Verification

`dotnet build OpenAlexFacultyPublications.sln --configuration Release`

`dotnet test tests/OpenAlex.Tests/OpenAlex.Tests.csproj --configuration Release`

The added tests cover identity validation/normalization, DOI URL formats,
deduplication across sources, ordering, URL escaping, more than 6,000 results,
missing faculty vs. no DOI, and page validation/error/cancellation behavior.
CI now also checks pull requests targeting `release/**` branches.

Before deployment, use the actual SQL Server database to compare a known
faculty member's result with the current synchronized relationships and accepted
OpenAlex decisions. Check a userid with no DOI, an unknown userid, an invalid
userid, case/email input variants, and a DOI repeated across sources. Verify
that incomplete snapshots and nonaccepted native decisions remain excluded.
Unit tests and a successful build do not establish SQL-backed data correctness.

### Live database preflight

In SQL Server Management Studio, select the database used by the app
(`OpenAlexIngestion_Test` in the current configuration), then run
`sql/451_FacultyDoiLookup_Preflight_SQL2016.sql`. It reports the connected
server/database, checks every column used by the lookup, and lists up to ten
candidate faculty userids from each available source. It changes no application
data or schema. An absent source may be optional; missing columns within an
existing source must be investigated before testing results.

Share the schema-status and missing-column results and choose a known faculty
userid from the candidate results. Next, compare that faculty member's DOI rows
with `/Reports/FacultyDois` using the same database. This preflight does not by
itself establish that the DOI results are correct.
