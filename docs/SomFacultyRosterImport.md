# SOM faculty roster bootstrap

This import establishes a ComputingID-based roster independently of external
researcher profiles. It creates `openalex.FacultyRoster` plus two import-audit
tables. It does not insert or change `openalex.Faculty`, provider identities,
publication decisions, worker jobs or pilot cohorts.

The current application Faculty page reads its native faculty table. Loading
this roster does not deploy a new app view or add the full roster to matching
jobs. The verification query inspects the permanent roster directly.

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
