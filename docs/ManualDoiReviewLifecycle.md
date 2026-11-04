# Manual DOI review lifecycle (PATCH022F)

## Problem and scope

PATCH022C deliberately creates a review candidate without creating an Academic
Online or roster publication relationship. The deployed PATCH021U review-sync
procedure reads only those automatic relationships. A normal nonempty sync
therefore retires the manual candidate, and resubmission returns `AlreadyExists`
without restoring it.

`sql/457_PATCH022F_ManualDoiReviewLifecycle_SQL2016.sql` adds a manual source to
that procedure. It is an additive SQL fix, not a replacement for the user's
PATCH022 application commits. It does not change authorization; the separate
disabled/revoked-user review blocker still needs resolution before the broader
write-enabled feature is considered merge-ready.

Prerequisites are the deployed PATCH021U review objects, the installed **450
NoCandidates fix**, and the PATCH022C submission audit/table and publication
objects. The installer requires the supplied automatic AO/roster review universe;
it stops if that view already appears to integrate manual submissions. Those
existing database dependencies are not provisioned by this migration.

## Preservation policy

- Only a `CreatedPendingCandidate` audit matching candidate ID, faculty key,
  publication key, and department establishes manual origin. An `AlreadyExists`
  audit or a mutable `RelationshipSource` label alone is insufficient.
- The faculty must still belong to the active department's current, complete
  faculty snapshot. Candidates for ineligible faculty are not kept active merely
  because an old manual audit exists.
- An automatic source for the same faculty/publication pair takes precedence.
  Otherwise the manual source uses canonical publication metadata and the
  original submission's OpenAlex author. The durable audit survives automatic
  source metadata overwriting the candidate's manual label.
- The existing sync UPDATE restores a retired manual candidate **in place** and
  preserves its ID and any existing review decisions. No duplicate candidate,
  review decision, or AO/roster publication relationship is created by the patch.
- Manual-only associations have `IsCurrentSource=1` but
  `IsInCurrentReportingWindow=0`: they remain reviewable, but are not represented
  as promoted reporting-store relationships. Use the workspace's **Any** reporting
  window filter when checking them. Native associations retain native metadata
  and reporting-window flags.

The six-column sync result is unchanged. `SourceRows` now includes eligible
manual rows as well as native rows. No new UI outcome or persistent log type is
introduced. Resubmission of a restored row still returns `AlreadyExists`.

A manual row must not hide unexpected automatic-source loss. If the native source
is empty, a manual-only sync is allowed only for the same complete, promoted,
zero-publication snapshot proven by 450: all discovered scholars processed,
positive matching current faculty count, no pagination safeguards, no existing
AO/roster publication links, and every existing department candidate accounted
for by an eligible manual creation audit. Otherwise 51101 remains. With no manual
rows, the original 450 `NoCandidates` behavior is unchanged. Ordinary automatic
retirement, duplicate-source rejection, and other-department isolation remain.

## Get the additive fix

Merge the fix branch **into your existing local release branch**, retaining your
validated PATCH022 commits. Do not replace that branch with this fix branch's
older application baseline. From the repository root in PowerShell, first verify
`git status` is clean and `git branch --show-current` is
`release/v3e-patch021-merge-readiness`. Then:

```powershell
git fetch origin
if ($LASTEXITCODE -ne 0) { throw 'Fetch failed' }
git merge --no-ff -m "Preserve manual DOI candidates during review sync" origin/fix/manual-doi-review-lifecycle
if ($LASTEXITCODE -ne 0) { throw 'Merge needs attention; do not proceed to SQL' }
```

The fix consists only of new SQL, a test fixture/suite, and this document. Nothing
has been applied to the application database by fetching or merging these files.

## Run the regression suite first

Cloud checks: Microsoft's SQL Server 2016 parser accepted the installer, rollback,
actual PATCH022C submission fixture, resulting deployed-procedure text, and fully
expanded SQLCMD suite. Mechanical text checks confirmed that rollback restores
the prior 450-patched procedure, including its original DML and six-column result,
with both CREATE and ALTER metadata headers and leading/nested comments.

**SQL execution has not been verified in the cloud.** The Windows SQL Server is
not reachable here, and the available SQL Server image download was blocked at
`centralus.data.mcr.microsoft.com`. Syntax/text checks do not establish runtime
success. Run this suite locally before deploying 457.

From the repository root, create a **new scratch database**, never the app DB:

```powershell
$testServer = 'DESKTOP-SPA6UI1'
$regressionDb = 'OpenAlexReviewSyncRegression_' + [guid]::NewGuid().ToString('N')
sqlcmd -S $testServer -E -b -Q "CREATE DATABASE [$regressionDb];"
if ($LASTEXITCODE -ne 0) { throw 'Scratch database creation failed' }
sqlcmd -S $testServer -E -b -d $regressionDb -i tests/sql/ManualDoiReviewLifecycle_Regression.sql
if ($LASTEXITCODE -ne 0) { throw 'Manual DOI lifecycle regression failed' }
```

No `-v` assignment or `RepoRoot` variable is needed. Use the existing trusted SQL
connection configuration. The suite refuses an existing application schema or a
database outside the scratch naming convention, and leaves the scratch DB for
inspection. Do not rerun it against the same scratch DB.

It first runs all original 450 checks, then reproduces the manual retirement bug
using the **actual submitted PATCH022C procedure** retained as a test-only fixture.
It checks in-place recovery, repeated sync/resubmission, native/manual handoffs,
ordinary retirement, manual-only expected-empty snapshots, source-loss safety,
audit provenance, current faculty eligibility, department isolation, rollback,
and reapplication. Synthetic objects substitute for the externally deployed views
and tables; the application UI/authorization are not exercised by these SQL tests.

The old `ALL REGRESSION CHECKS PASSED` appears partway through. The required
**final** message is `ALL MANUAL DOI LIFECYCLE CHECKS PASSED`, with exit code **0**.

## Deploy and verify after the tests pass

1. Pause the department-refresh service and other review-sync writers. Back up the
   application database according to normal practice and save the current review
   procedure definition (Script Stored Procedure as ALTER).
2. In SSMS, select the **application database**, then execute only
   `sql/457_PATCH022F_ManualDoiReviewLifecycle_SQL2016.sql`. Do not run the test
   fixture or regression suite there. The installer changes the procedure only;
   it does not execute synchronization or recover candidate data by itself.
3. Select a department with a known manual submission. Record its candidate ID,
   faculty/publication keys, current flag, and review status. Run the normal
   `academiconline.usp_SyncFacultyPublicationReviewCandidatesForDepartment` with
   that explicit department ID. An eligible retired manual row should return to
   `IsCurrentSource=1` with the same ID and unchanged decision. An unexpected
   51101 is a safety stop: investigate the snapshot/source, not the guard.
4. Verify the row in the application's review workspace using **Any** reporting
   window. Sync again and resubmit the same DOI; expect one current candidate
   and `AlreadyExists`, not a new candidate. Check another ordinary native
   candidate and the department's reporting store for unintended changes.
5. Resume the service and inspect the next relevant refresh and review-sync logs.
   No bulk candidate recovery, refresh, or deployment is performed by this cloud
   task. Old operational errors remain historical records.

## Rollback

With writers paused, run
`sql/rollback/457_PATCH022F_ManualDoiReviewLifecycle_Rollback_SQL2016.sql`.
It removes only this marked block and retains the installed 450 NoCandidates
guard. Roll back **457 before 450** if removing both. Installation and rollback
use ALTER, preserving the procedure object and grants; unfamiliar layouts stop
rather than guessing.

Rollback does not undo candidate changes from intervening synchronizations.
Subsequent native syncs can again retire manual rows. Inspect independent
procedure edits before using a saved definition; do not restore over unrelated
changes blindly.
