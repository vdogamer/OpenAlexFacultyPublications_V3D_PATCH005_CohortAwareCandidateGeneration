# Read imported faculty and DOI associations in the existing pages

Faculty and DOI Lookup now read the separately loaded
`academiconline.RebuildImportedFaculty` and
`academiconline.RebuildImportedFacultyDoi` tables in the application's existing
database connection. These are optional sources: databases without either table
retain the existing behavior. This change does not import data or change database
records, collection jobs, cohorts, authentication, or publication decisions.

## Directory behavior

The directory retains its existing roster/native rows. An imported faculty row
shares a display row with a roster ComputingID, a native ComputingID, or an exact
canonical `ComputingID@virginia.edu` native email when the native ComputingID is
absent. A contradictory native ComputingID prevents that email from attaching
the import. Names, directory email aliases, and native SourceFacultyKey values
do not establish an import identity link. Existing unresolved native records
remain separate.

Roster names and departments remain the primary directory display values.
Imported names/departments are available for search, and an alternate imported
name is shown with its source label. Import-only records display their supplied
name, ComputingID, and department. Their email, appointment, and active status
remain unknown. Counts distinguish roster, additional native, and additional
import-only records. Search is applied before pagination, including source-name
and source-department searches and literal wildcard characters.

Native navigation uses a stored ComputingID or canonical institutional email.
SourceFacultyKey remains searchable but does not create a directory ComputingID
or a View DOIs link. This prevents arbitrary native keys from claiming a newly
imported ComputingID. A source key alone also cannot attach accepted native
publications to a user present in the rebuild. The legacy key fallback remains
for lookups outside this imported population. Explicit native ComputingIDs and
existing confirmed roster-to-native links continue to identify native results.

## DOI behavior

The repository adds an indexed ComputingID lookup and LEFT JOIN to the imported
DOI source. A known imported faculty member without DOI rows remains known.
The source is labeled **Academic Online import**, separately from synchronized
publications and accepted OpenAlex publications. Import presence is distinct
from roster membership. A partial import schema is reported as an error rather
than an unknown person or empty result.

The service normalizes/deduplicates DOI keys across sources for the requested
faculty member; coauthors keep their own associations to the same DOI. Existing
titles win over missing imported titles when publication years are both unknown.
Missing titles display `(Title not supplied)`; missing years remain unknown.
Stored source attributions are retained. Importing an association does not assert
that it passed an OpenAlex review or that the displayed results form a complete
career publication history.

All SQL values remain parameters. Razor encodes names and other stored text.
Reader authorization, invalid input, unknown faculty, known-zero and load-error
states remain in their existing request handlers.

## Verification

The .NET workflow builds Razor and runs service and HTTP tests. The SQL workflow
uses disposable synthetic databases at compatibility level 130 to exercise the
production repositories with optional/partial import sources, import-only
schemas, mixed collations, native/roster overlaps, source-name searches, 205-row
pagination, DOI coauthors, metadata gaps, stale/rejected source rows, conflicting
native identifiers and source-key collisions. Existing no-import tests remain.
CI is enabled for pull requests targeting the current UVA navigation branch.

After applying this code locally, build and restart the existing web app against
the database where the source import succeeded. Search a known import-only
ComputingID, follow View DOIs, then check a zero-DOI faculty member and an overlap
with the roster. Compare the displayed normalized DOI count with the stored
associations for that person and verify the source labels. No new migration or
second faculty import is required for this page integration.
