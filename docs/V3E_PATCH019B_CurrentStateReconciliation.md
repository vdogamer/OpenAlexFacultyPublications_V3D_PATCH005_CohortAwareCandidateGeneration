# V3E PATCH019B — Department Current-State Reconciliation

## Goal

Make Academic Online department reporting behave like a trustworthy current snapshot instead of an append-only cache.

PATCH019B preserves historical rows while ensuring manager-facing reports use only the latest promoted complete department snapshot.

## Core rule

A partial sync may refresh source rows, but it does not replace the department's current reporting snapshot.

Only a run with:

- `RunStatus = Completed`
- `IsCacheComplete = 1`

may be promoted.

## Database migration

After PATCH019 and PATCH019A, run:

`sql/415_V3E_PATCH019B_CurrentStateReconciliation_SQL2016.sql`

Then validate with:

`sql/416_V3E_PATCH019B_CurrentStateReconciliation_Diagnostics_SQL2016.sql`

## New current-state structures

PATCH019B adds:

- `academiconline.Department.CurrentSnapshotRunId`
- `academiconline.Department.CurrentSnapshotAtUtc`
- `academiconline.ScholarDepartment.IsCurrent`
- `academiconline.ScholarDepartment.CurrentSnapshotRunId`
- `academiconline.SyncRun.SnapshotPromoted`
- `academiconline.DepartmentPublication`
- `academiconline.DepartmentScholarPublication`

The department-publication bridge identifies publications in the promoted department snapshot.

The department-scholar-publication bridge preserves faculty attribution for that exact snapshot. This prevents a later partial refresh from changing faculty names on the still-current complete snapshot.

## Backfill behavior

The migration finds the latest completed cache-complete sync for each existing department and promotes it as the initial current snapshot.

For the validated Cell Biology data, PATCH019B should backfill the existing complete run rather than requiring another Academic Online API retrieval merely to initialize the snapshot layer.

## Future sync behavior

During every department sync, the worker continues to upsert source data and historical relationships.

It also stamps:

- department/publication observations;
- department/scholar/publication observations.

After the run is marked complete:

- if `IsCacheComplete = 0`, no current snapshot changes;
- if `IsCacheComplete = 1`, the run is promoted transactionally.

Promotion reconciles current state:

- scholars seen in the promoted run become current;
- previously current scholars not seen in the promoted run become non-current;
- publications seen in the promoted run become current;
- previously current publications not seen in the promoted run become non-current;
- department-specific faculty/publication attribution is reconciled the same way.

Historical rows are retained for provenance.

## Reporting behavior

`/Reports/DepartmentPublications` now reads only the promoted current snapshot.

A later partial sync therefore does not silently change a manager-facing report that was based on a known-good complete snapshot.

The report labels its metadata as the current snapshot rather than merely the latest sync attempt.

## Scope

Current-state correctness is scoped to the promoted run's configured ingestion window.

For the current Cell Biology validation:

`2025–present; VerifiedOnly = false`

means the current snapshot is complete for that configured Academic Online retrieval scope, not for all publication history.

## Validation target

Immediately after applying PATCH019B, the migration should backfill the existing Cell Biology complete run.

Expected shape:

- current snapshot run = existing complete PATCH019A run;
- current scholars = 14;
- current publications = the current 2025-present unique publication count;
- snapshot promoted = 1.

No API sync is required for this initial PATCH019B validation.
