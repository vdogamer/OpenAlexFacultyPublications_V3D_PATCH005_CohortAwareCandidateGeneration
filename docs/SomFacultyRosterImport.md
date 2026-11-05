# SOM faculty roster bootstrap

This import establishes a ComputingID-based roster independently of external
researcher profiles. It creates `openalex.FacultyRoster` plus two import-audit
tables. It does not insert or change `openalex.Faculty`, provider identities,
publication decisions, worker jobs or pilot cohorts.

The Faculty page reads the imported roster and retains native faculty records
without a confirmed roster link as separate rows. ComputingID, name, email and
department searches are paginated, so the full roster is accessible beyond the
old 200-row display limit. A confirmed `NativeFacultyId` combines those two
records; names alone never combine records. The page requires Reader, Reviewer
or Admin access and labels identity statuses as the baseline recorded at import.

Faculty DOI Lookup also recognizes a ComputingID directly from the roster,
including members without any stored publications. It reports these as found
with zero DOIs rather than unknown faculty. Publication retrieval retains its
current-snapshot and accepted-decision rules, and may follow a native link
already confirmed during import. It does not infer links from roster names or
directory-email aliases. The roster contributes identity information, never a
DOI. The roster name is used when publication sources carry a different name.

These are read-only page changes. Loading or displaying a person does not enroll
them in matching jobs, fetch additional publications, or create provider profiles.

## Build a private payload

Run the standard-library Python builder with the management-cache and person
exports from the same ResearchFacultyDirectory export run:

```sh
python3 scripts/build-som-roster-import.py \
  --roster /private/RFD1.csv \
  --profiles /private/RFD2.csv \
  --output-dir /private/roster-load
```

The output directory must be outside this repository. The resulting SQL, CSV
and ZIP contain private directory records and must not be committed. An optional
`--baseline` comparison JSON preserves previously assessed local-identity states,
with the evidence-export run ID. `--source-server` records the server name from
the export Messages. These are provenance fields, not connection credentials.

The working population is the primary-appointment cache group `MBU=MD` and
`FacultyType=Faculty`. The builder checks the expected candidate and quarantine
counts, unique usernames, profile-ID consistency, field lengths and source run.
Nonstandard usernames and explicit User Test accounts are quarantined. Annotated
email values retain their raw text; a single complete address can be extracted,
and an incomplete value remains null rather than becoming an invented address.

This validates the exported records and their identity keys. It does not certify
the HR feed's freshness, employment status, or complete joint-appointment scope.
Directory account status is preserved separately from those questions.

## Apply locally

Open the generated `Load_SOM_Faculty_Roster.sql` in SSMS connected to the local
`OpenAlexIngestion_Test` database. Run the entire file. The default mode applies
the authorized roster load after validation. Set the first context value to `1`
for an optional preview. Preview creates no permanent objects.

The loader verifies row and full-payload SHA-256 fingerprints, takes an import
lock, and writes the roster plus audit membership in one transaction. An identical
rerun reuses the existing roster keys. Different source data for an existing key
stops the load without overwriting existing records. This is a bootstrap, not an
automatic refresh or deletion policy.

Native-faculty links use exact identifiers or emails and must be one-to-one.
Name-only and ambiguous links remain pending review. An unresolved publication
identity does not prevent an otherwise valid ComputingID from entering the roster.

The final result must show `Committed`, the expected verified roster count, and
the quarantine count. Earlier plan grids are not evidence of a committed load.
Run `sql/455_VerifySomFacultyRoster_SQL2016.sql` to inspect the stored records.

## Verification

The SQL integration workflow uses synthetic records only in a disposable SQL
Server container with database compatibility level 130. It checks preview,
first import, idempotent rerun, exact versus ambiguous/name-only native links,
quarantine, fingerprint tampering, conflicting existing source data, rollback
after a write-time constraint failure, and preservation of native faculty and
publication decisions. No user roster data is uploaded to CI.

The same workflow runs .NET repository integration tests against disposable SQL
Server databases at compatibility level 130. These cover roster-only and
pre-import databases, native schema aliases, confirmed-link deduplication,
identifier/department search, literal wildcard characters, pagination beyond 200
rows, and preserved current/accepted publication results. HTTP tests cover access
control, encoded content, navigation and distinct missing/empty/error states.

## Update the local web application

After checking out the application change, stop the existing web process using
port 5000, keep the existing local database configuration, then build and run:

```powershell
dotnet build OpenAlexFacultyPublications.sln --configuration Release
dotnet run --project src/OpenAlex.Web/OpenAlex.Web.csproj --configuration Release --no-launch-profile --urls http://localhost:5000
```

Sign in with a Reader, Reviewer or Admin account. Check `/Faculty`, search a
loaded ComputingID, and use its **View DOIs** link. A member without stored DOI
records should be found with zero DOIs. Existing known DOI controls should retain
their publication results. No roster reload is needed for this page update.
