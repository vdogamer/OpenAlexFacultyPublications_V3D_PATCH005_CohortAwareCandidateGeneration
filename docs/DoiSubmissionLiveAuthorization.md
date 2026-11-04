# DOI submission live authorization (PATCH022G)

Baseline: the user's published `review/patch022-authorization-baseline` commit
`572bf12`, including the validated PATCH022 application commits and lifecycle
fix 457. The fix branch is `fix/manual-doi-live-authorization`; it does not update
the remote release branch or apply anything to the application database.

## Security behavior

Previously the department authorization helper trusted an Admin cookie without
checking the current account. For Reviewers it checked the department-access row
but not live enabled status or role membership. The DOI submission procedure
validated faculty/work association, but did not authorize the submitting account.

The fix has three layers:

- Every authenticated cookie request checks the current account ID, canonical
  username, enabled status, and roles. Missing/disabled/replaced accounts and
  changed roles reject and delete the old cookie before role policies run. There
  is no validation-interval cache. This also prevents using an old Admin cookie
  to reach the role-management UI and undo a revocation.
- Department checks and allowed-department lists use current database profiles
  and current role/access rows, including for Admins. The signed ID and username
  must identify the same account. Anonymous or malformed identities fail closed.
- The DOI submission procedure independently checks the signed account ID and
  username, enabled status, current Admin/Reviewer role, and Reviewer department
  access **inside its existing serializable transaction, before publication DML**.
  HOLDLOCK reads serialize conflicting account/role/access changes with the write,
  including with READ_COMMITTED_SNAPSHOT enabled. A revocation completed before
  this check denies the write; simultaneous changes follow transaction locking.
  No concurrent two-connection lock test has been executed in this cloud task.

The database revalidates the actual faculty/department association as before and
also refuses to return an existing candidate belonging to another department.
Current enabled Admins retain global authority; Reviewers need active CanReview
access to the selected identity's department. A Reader/access-row combination is
not sufficient. Denials roll back publication/candidate/audit writes, including
an `AlreadyExists` attempt after revocation.

The actor ID is taken from the protected NameIdentifier claim, never a form field,
and passed alongside the server-authenticated username through the service and
repository. SQL error 51403 becomes an authorization denial, not a successful
submission redirect. Browser requests retain the app's existing redirects to
Login or AccessDenied. Anti-forgery validation remains enabled.

Disabled users cannot sign in through demo or Shibboleth header mode, even if the
legacy `Auth:RequireEnabledUser` flag is false. Any role change (including a new
grant) invalidates the existing cookie; demo users sign in again. In Shibboleth
mode a fresh trusted-header sign-in can happen automatically and external group
mappings remain authoritative: revoke the upstream entitlement too when needed.
Department-only revocation does not require logging out; live scope checks deny it.

This introduces live database reads during cookie validation. Database failures
do not fall back to cached role claims. Cookie validation is a per-request check,
not a transaction lock over every existing application endpoint; the transactional
write guarantee added here is specifically for DOI submission.

## Coordinated application + SQL deployment required

`sql/458_PATCH022G_DoiSubmissionLiveAuthorization_SQL2016.sql` adds
`@SubmittedByAppUserId bigint = NULL` to the deployed PATCH022C submission module
and inserts the authorization/candidate-scope checks. A missing/nonpositive actor
ID is rejected. The default exists only so legacy callers fail closed with an
authorization error, not so they can continue submitting anonymously.

Deploy the matching Web build and SQL 458 together while the Web app/submission
writers are stopped. The new application needs the new parameter; the old
application intentionally cannot submit after SQL 458. The procedure object,
grants, original faculty validation, staging, four-column result, and idempotency
are preserved. Installation is DDL only and reapplication is a no-op. Rerunning
the checked-in 452 script now refuses to drop an authorization-patched module.
Do not use an older checkout's 452 script to replace it.

The pre-existing auth and review database objects must already exist. This fix
does not seed users/roles/access or solve the separate missing/versioned-baseline
DDL review item. No acceptance decision or automatic source link is introduced.
The review-sync procedure and the installed 450/457 behavior are unchanged.

## Get and test locally

With a clean working tree on the local branch used to publish the review baseline,
run from the repository root:

```powershell
git fetch origin
if ($LASTEXITCODE -ne 0) { throw 'Fetch failed' }
git merge --ff-only origin/fix/manual-doi-live-authorization
if ($LASTEXITCODE -ne 0) { throw 'Branch diverged; stop and review before proceeding' }
dotnet build OpenAlexFacultyPublications.sln --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet test tests/OpenAlex.Tests/OpenAlex.Tests.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { throw '.NET tests failed' }
```

If Windows reports locked Web binaries, stop that VS Code application launch and
repeat the build. The refresh service need not be stopped for isolated tests.

Cloud validation: Release build succeeded with the seven existing nullable
warnings; all **34 .NET tests passed** (14 prior tests and 20 authorization tests).
The new tests exercise real protected cookies, the actual Razor handler and
antiforgery, with a fake live-account store and fake submission service. They do
not execute SQL or call OpenAlex. Microsoft ScriptDom's SQL Server 2016 parser
accepted migration 458, the resulting submission module, embedded authorization
queries, and the fully expanded SQLCMD regression suite. Mechanical checks
confirmed unchanged original procedure body outside the three marked insertions
and placement of live authorization before transactional publication writes.

**SQL execution is not cloud-verified.** The Windows database is unreachable
from here; the previously attempted SQL Server image download was blocked at
`centralus.data.mcr.microsoft.com`. Run the following on your local server in a
**new scratch database**, never the app database:

```powershell
$testServer = 'DESKTOP-SPA6UI1'
$regressionDb = 'OpenAlexReviewSyncRegression_' + [guid]::NewGuid().ToString('N')
sqlcmd -S $testServer -E -b -Q "CREATE DATABASE [$regressionDb];"
if ($LASTEXITCODE -ne 0) { throw 'Scratch database creation failed' }
sqlcmd -S $testServer -E -b -d $regressionDb -i tests/sql/DoiSubmissionAuthorization_Regression.sql
if ($LASTEXITCODE -ne 0) { throw 'DOI submission authorization SQL regression failed' }
```

No RepoRoot variable or -v assignment is needed. The suite refuses an existing
application schema or a database outside the scratch naming convention and leaves
the scratch database for inspection. It includes all prior 450/457 tests, then
reproduces the original disabled-submitter defect, applies 458 twice, verifies
DDL-only/grant preservation, and tests disabled/missing/replaced accounts, removed
roles, demotion, revoked/wrong-department access, actor mismatch, legacy calls,
unauthorized AlreadyExists/candidate scope, authorized Admin/Reviewer submissions,
and interaction with manual lifecycle synchronization.

Earlier success messages appear partway through. Require the final message
`ALL DOI SUBMISSION AUTHORIZATION CHECKS PASSED` and process exit code **0**.
These synthetic SQL fixtures do not provision or test every external baseline view.

## Application-database validation after local tests pass

1. Stop the Web app and any other DOI submission writers. Pause the refresh service
   if performing the combined lifecycle smoke test; keep SQL Server running.
2. Follow the normal database backup process. Save the current DOI submission
   procedure as ALTER and retain the prior application build for recovery.
3. Select the application database in SSMS and execute **only SQL 458**, not a
   test fixture or regression suite. Build/deploy the matching application while
   writers remain stopped. Installation should report live authorization installed.
4. Start the Web app and use disposable test accounts, not your last enabled Admin.
   Keep a separate authorized Admin session available. Sign in as a Reviewer and
   obtain a DOI submission page/token. Disable the account or remove its role in
   the separate Admin session, then submit using the original session. Expect
   Login/AccessDenied and no new publication/candidate/submission-audit writes.
5. Repeat for an Admin whose role is revoked, and for a Reviewer whose department
   access alone is revoked. Confirm a current enabled Admin and authorized Reviewer
   can still submit/resubmit, cannot redirect a candidate to another department,
   and that normal review synchronization retains the manual candidate.
6. Restore the disposable test-account configuration and resume any services that
   were paused. Review the relevant logs. Retain old operational error history.

Do not disable or remove the role of the last enabled Admin. These changes are
not automatically deployed by this cloud task; report local SQL/UI results before
considering the broader release merge ready.

## Recovery

Keep submission writers stopped if either half of deployment fails. Prefer a
forward fix or leave submissions unavailable rather than restoring the vulnerable
procedure. Reverting application and SQL must be coordinated: the new build needs
the new parameter and the old build is deliberately denied by SQL 458. Restoring
the saved original module removes the security boundary and requires explicit
operator risk acceptance; no automatic security-downgrade script is provided.
Do not remove the independent 450/457 fixes during authorization recovery.
