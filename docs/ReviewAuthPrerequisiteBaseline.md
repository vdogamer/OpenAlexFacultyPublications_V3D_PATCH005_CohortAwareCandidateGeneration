# PATCH022 review/auth prerequisites and enabled role state

## What is versioned

The user's November 4, 2026 schema-only review export and subsequent pasted core
auth definitions are captured in
`sql/baselines/PATCH022_ReviewAuthPrerequisites_SQL2016.sql`. It contains **18 real
tables, 3 views, and 2 procedures**, including the current 450/457 review-sync
guards. These are deployed definitions, not the simplified regression fixtures.

The manifest beside it records source hashes, normalization, object inventory,
installation order, and explicit exclusions. The core-auth hash identifies the
whitespace-formatted transcription of the pasted text, not an original uploaded
file. The baseline removes a hardcoded USE target, SSMS timestamps and its
unresolved alias-c warning comment, and five SQL 2019-only
`OPTIMIZE_FOR_SEQUENTIAL_KEY=OFF` options. Omitting that OFF-only optimization
option preserves SQL 2016 index behavior. It adds dbo-owned schemas, required
SET options, an empty/system-database guard, SQLCMD fail-fast, and transactional
schema creation. Defaults, keys, indexes, FKs, checks, views and procedures remain.
The export did not script collations; string columns inherit the target database's
default collation. Choose an appropriate database collation before installing
outside the current server; source column-level collation metadata was not supplied.

The ordered installer adds the already versioned role-management extensions,
manual submission and freshness audit objects, then security migrations 458/459.
It refreshes all three real views and the three feature procedures to check binding.
The resulting scoped schema has **21 tables**. No source rows, users, passwords,
role definitions/memberships, database principals or permission grants are seeded.
Object-level GRANT/DENY statements were absent from the supplied export; none are
invented. Runtime principals and least-privilege permissions remain operator work.

**This is not a full OpenAlex ingestion/dashboard database bootstrap.** Legacy V1
OpenAlex tables/reporting views, scheduler/discovery/ingestion routines, source
data, credentials, service setup and unrelated application features remain outside
this feature subset. Do not point the entire app/service at this empty baseline
and assume every older endpoint or ingestion job has its dependencies. This
closes the versioning gap for the reviewed PATCH022 auth/review feature objects,
not the previously external V1 foundation as a whole.

## Important follow-up discovered in the real schema

The exported core tables have `AppRole.IsEnabled`, `AppUserRole.IsEnabled`, and
`AppUserRole.RevokedAtUtc`. Patch 458 checked current membership but ignored those
state fields. Its earlier synthetic fixtures did not represent these states.

The updated Web/Data code filters effective roles by all three conditions. Cookie
validation consequently rejects old sessions after a role definition or assignment
is disabled, or a revocation timestamp is set. Department checks and decision
authorization use the same predicates. Role-management lists and last-Admin counts
exclude inactive/revoked roles; an explicit authorized UI grant can reactivate an
existing disabled assignment and clear its revocation metadata, while globally
disabled role definitions cannot be selected. No rows are reactivated by deployment.

`sql/459_PATCH022H_EnabledRoleAuthorization_SQL2016.sql` alters only the live role
lookup inside the already-installed 458 submission procedure. It preserves object
identity/grants, transaction locking, actor binding, department scope, original
DML, and result contracts. A separate migration is required: rerunning 458 alone
would report already installed and would not update a deployed v1 guard.

## Get and build

The branch `fix/review-auth-prerequisite-baseline` builds on authorization commit
`64f6dae`. Stop a VS Code Web app using auto-reload before merging, so source changes
do not partially deploy themselves. Keep SQL Server running. With a clean local
branch containing that authorization fix, run from the repository root:

```powershell
git fetch origin
if ($LASTEXITCODE -ne 0) { throw 'Fetch failed' }
git merge --ff-only origin/fix/review-auth-prerequisite-baseline
if ($LASTEXITCODE -ne 0) { throw 'Branch diverged; stop and review' }
dotnet build OpenAlexFacultyPublications.sln --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet test tests/OpenAlex.Tests/OpenAlex.Tests.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { throw '.NET tests failed' }
```

## Validate on two different new scratch databases

The upgrade regression runs all older synthetic 450/457/458 checks, reproduces
the disabled-assignment gap under 458, applies 459 twice, and checks disabled
Admin/Reviewer roles, disabled assignments and timestamp-only revocation without
unauthorized writes. It also checks the existing grant and authorized lifecycle.

```powershell
$testServer = 'DESKTOP-SPA6UI1'
$upgradeDb = 'OpenAlexReviewSyncRegression_' + [guid]::NewGuid().ToString('N')
sqlcmd -S $testServer -E -b -Q "CREATE DATABASE [$upgradeDb];"
if ($LASTEXITCODE -ne 0) { throw 'Upgrade scratch database creation failed' }
sqlcmd -S $testServer -E -b -d $upgradeDb -i tests/sql/EnabledRoleAuthorization_Regression.sql
if ($LASTEXITCODE -ne 0) { throw 'Enabled-role upgrade regression failed' }
```

Require the final message `ALL ENABLED ROLE AUTHORIZATION CHECKS PASSED` and exit
code 0, not an earlier included suite's success message.

The fresh-baseline test installs the real exported schema and all ordered feature
scripts, verifies binding and trusted constraints, and checks zero seeded data.
Only then does the **test** add synthetic users/roles/faculty in the scratch DB.
It exercises actual faculty/search/universe/status views, defaults/FKs, authorized
DOI submission, six inactive/revoked-role cases, review decision history/current
uniqueness, and preservation through synchronization without automatic publication.

```powershell
$baselineDb = 'OpenAlexPrerequisiteValidation_' + [guid]::NewGuid().ToString('N')
sqlcmd -S $testServer -E -b -Q "CREATE DATABASE [$baselineDb];"
if ($LASTEXITCODE -ne 0) { throw 'Baseline scratch database creation failed' }
sqlcmd -S $testServer -E -b -d $baselineDb -i tests/sql/ReviewAuthBaseline_Regression.sql
if ($LASTEXITCODE -ne 0) { throw 'Real-schema baseline regression failed' }
```

Require `ALL REVIEW AUTH BASELINE CHECKS PASSED` and exit code 0. Do not use the
same database for both suites or reuse a partial test database. Both entry points
enforce their scratch-name prefixes. They leave databases for inspection and
never drop a database. Use the existing trusted SQL connection configuration.

The baseline installer is **new-empty-database only**, not an upgrade. Always use
SQLCMD mode with `-b` and the repository root as the working directory. Its
schema creation is transactional, but a later feature-install failure can leave
a partial scratch installation; stop and inspect, then test in another fresh DB.
Never run the snapshot, installer or test fixtures against the application DB.

## Existing application update after tests pass

1. In SSMS, select the current application database and run the **read-only**
   `sql/460_PATCH022H_ReviewAuth_Readiness_SQL2016.sql`. The new 459 guard should be
   missing before installation; the existing 450/457/458 guards and role-state
   columns should be present. `EffectiveEnabledAdminCount` must be positive before
   updating the app. If zero, stop and have an authorized operator resolve the
   actual role/account state; do not blindly enable accounts or grant Admin.
2. Stop the Web app/other DOI writers. Back up the database and save the current
   DOI submission procedure as ALTER. Deploy the matching Web build and execute
   **only SQL 459** against the app DB. Do not rerun 452, reinstall the snapshot,
   or change roles/source data. The refresh service does not need new binaries;
   pause it only if your combined smoke-test maintenance procedure requires it.
3. Start the Web app. With disposable accounts and a separate authorized Admin
   session, test disabling a role/assignment and setting its revocation state.
   Original cookies must not authorize a new DOI write. Also verify normal active
   Admin/Reviewer submissions. Never disable the last effective Admin.
4. Run read-only 460 again and expect every required guard/column/object present.
   Resume anything deliberately paused after validation and report the results.

A revocation timestamp denies the assignment even if its IsEnabled bit remains 1.
This is deliberately fail-closed for inconsistent rows. An authorized manual
re-grant must clear revocation state, not just flip the bit. Existing 458 callers
retain the same signed actor parameters. No automatic security-downgrade rollback
or production user/role seeding is provided.

## Validation evidence and limits

Cloud .NET tests: **40 passed**, including six additional real-cookie cases for
disabled role definitions, disabled assignments and timestamp-only revocation.
The HTTP tests use a fake live-account store and cannot execute SQL. Release
build warnings remain the existing nullable warnings, unrelated to this change.

Microsoft's SQL Server 2016 parser accepted the snapshot, 459, readiness script,
expanded ordered installer, both expanded new SQL suites, and modified auth SQL.
Token comparisons confirmed preservation of all **189 review-export statements**
and **20 normalized core-auth statements**, in order, and no unrelated changes
to the 458 module when adding the 459 predicate. The snapshot's sync body matches
the previously validated 450+457 body after ignoring formatting/comments/GO.

**SQL execution still requires the local runs above.** The cloud cannot reach the
Windows SQL Server and the SQL Server image download was previously denied by
network policy. Static SQL 2016 compatibility does not establish runtime success
on an actual SQL Server 2016 engine. The supplied export contains no application
data, and none has been read or modified by this cloud task. Do not mark the
broader release merge ready until local SQL and application validation are complete.
