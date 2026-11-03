# Expected-empty department review synchronization

## Problem and scope

Department 18's promoted run 353 processed 1/1 scholars, excluded 81 publication
rows under the configured 2025-onward year policy, and retained zero qualifying
publications. There are no stored publication relationships or review candidates
for that department. The subsequent review-sync procedure raises error 51101
because its source view is empty, even though this is an expected empty workload.

This patch targets the **deployed PATCH021U SQL procedure exported during
diagnosis**. That procedure and its caller are not present in the inspected
`3ed668c54d29` release checkout or the available remote branches. This is an
additive database hotfix for that deployed routine, not a bootstrap migration for
an unmodified copy of the release. It does not add the missing C# integration,
change year policies, enable jobs, address worker dependency lifetimes, or fix
the Errors page's choice of source table.

## Behavior

For an empty source, return the existing six-column result with all counts zero
only if all of the following hold:

- The active department references a completed, complete, promoted snapshot.
- The snapshot recorded zero qualifying publications, processed every discovered
  scholar, and hit neither pagination safeguard.
- A positive number of complete faculty rows match that snapshot; their count
  agrees with the number of discovered scholars, with recognized identity keys.
- No existing review candidates (including historical candidates) or publication
  relationships exist for the department.

SQL emits an informational `NoCandidates` message. The C# result contract is
unchanged; zero source/change/candidate counts represent the successful no-op.
There is no new persistent log entry or result column. In particular, do not claim
that a new named outcome will appear in the application's UI.

All other empty-source cases continue raising 51101. The duplicate-source guard
51102 and the nonempty update/insert/retire code remain unchanged. The patch
inserts a marked block before the recognized original THROW rather than replacing
the entire deployed procedure. Missing prerequisites, unexpected guard context,
or unfamiliar headers stop installation. Reapplication is a no-op. Module ALTER
preserves the existing procedure's grants.

## Test before applying to the app database

**Execution status in this cloud task:** the migration, rollback, original
procedure, resulting patched procedure, and regression suite passed Microsoft's
SQL Server 2016 syntax parser. The original body preservation check passed.
SQL execution tests have **not** run: SQL Server container downloads were denied
at `centralus.data.mcr.microsoft.com`, which is outside the current allowlist.
The remote Windows database is also unreachable from this task. Syntax checks
are not a substitute for the following execution tests.

The regression suite is self-contained, using the exported original procedure
and synthetic source views. It first reproduces 51101, applies the patch twice,
checks the zero-count contract and repeated no-op, rejects incomplete/unpromoted/
missing/failed snapshots, nonzero publication counts, pagination truncation,
missing or inconsistent faculty, existing current/historical candidates, and
orphaned relationships. It also checks cross-department isolation, ordinary
insert/update/retirement, duplicate rejection, rollback, and reapplication.

Run it on a **scratch SQL Server database**, never the app database. From a local
PowerShell terminal at the repository root, with SQLCMD available and the normal
trusted SQL connection settings for your local server:

```powershell
$testServer = 'DESKTOP-SPA6UI1'
$regressionDb = 'OpenAlexReviewSyncRegression_' + [guid]::NewGuid().ToString('N')
sqlcmd -S $testServer -E -b -Q "CREATE DATABASE [$regressionDb];"
if ($LASTEXITCODE -ne 0) { throw 'Scratch database creation failed' }
sqlcmd -S $testServer -E -b -d $regressionDb `
  -i tests/sql/EmptyDepartmentReviewSync_Regression.sql
if ($LASTEXITCODE -ne 0) { throw 'SQL regression checks failed' }
```

The script uses relative `:r` include paths from the repository root. No `-v`
assignment or `RepoRoot` environment variable is needed, and a checkout path
containing spaces is supported. Do not launch the command from `tests/sql`.

Use your existing trusted certificate configuration. The suite refuses a database
outside the `OpenAlexReviewSyncRegression` name/prefix or with an existing
`academiconline` schema. Each invocation above creates a uniquely named scratch
database, so a prior partial test run does not block a retry. It leaves scratch
databases for inspection and does not drop or overwrite any database. Inspect
and remove only identified scratch databases separately when finished.
The final success message must be `ALL REGRESSION CHECKS PASSED` with exit code 0.

## Apply locally after tests pass

1. Stop/pause the department-refresh Windows service and other review-sync writers.
2. In SSMS, select the actual app database. Save the current procedure's complete
   definition as a rollback backup (Script Stored Procedure as ALTER).
3. Open and execute `sql/450_EmptyDepartmentReviewSync_NoCandidates_SQL2016.sql`.
   It changes only the procedure definition; it does not execute a refresh.
4. With writers still paused, confirm Department 18 still has the diagnosed
   promoted zero-publication snapshot, no relationships, and no candidates.
5. Execute the procedure for that department:

   ```sql
   EXEC academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment
       @DepartmentId = 18;
   ```

   Expect the `NoCandidates` informational message and one row:
   DepartmentId=18, SourceRows=0, UpdatedRows=0, InsertedRows=0, RetiredRows=0,
   CurrentCandidateRows=0. No candidate changes should occur. If any prerequisite
   has changed, the original guard is deliberately retained; investigate rather
   than removing it.
6. Resume the service only after validation. Verify its next relevant run completes
   without a new `ReviewCandidateSyncFailed` for this expected-empty case.

Existing operational error history is retained. Applying this fix will not reduce
the dashboard's lifetime error count or retroactively rewrite run 353.

## Rollback

With writers paused, run
`sql/rollback/450_EmptyDepartmentReviewSync_NoCandidates_Rollback_SQL2016.sql`.
It removes only this marked guard and restores 51101 for empty sources, without
changing review data. If the procedure was independently edited after installation,
inspect the diff and use the saved ALTER definition as appropriate. A changed
guard format causes rollback to stop rather than guess.
