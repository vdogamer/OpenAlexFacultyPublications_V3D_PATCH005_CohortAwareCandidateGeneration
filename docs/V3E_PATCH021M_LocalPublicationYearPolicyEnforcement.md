# V3E PATCH021M — Local Publication-Year Policy Enforcement

## Starting point

PATCH021L normalized invalid numeric publication years such as -1 to NULL and
added trusted database CHECK constraints.

Validation still showed three NULL-year rows in the promoted Department of
Neurosurgery report even though the department refresh policy starts in 2025.

That exposed a policy-boundary issue:

- Academic Online may return rows with missing/invalid years despite a requested
  from/to year range.
- The worker persisted and attributed those rows without re-checking the year
  locally.
- The reporting view trusted the snapshot attribution without re-applying the
  department year policy.

## Goal

PATCH021M makes the department year window authoritative locally.

Upstream provider filtering remains an optimization, not a correctness boundary.

## Worker behavior

For every provider publication:

1. persist the publication in the global academiconline.Publication store;
2. preserve upstream YearText and RawJson;
3. for Academic Online, preserve the ScholarPublication relationship;
4. evaluate PublicationYear against the requested department year policy;
5. if either FromYear or ToYear is configured:
   - missing or invalid PublicationYear is not department-eligible;
   - years below FromYear are not eligible;
   - years above ToYear are not eligible;
6. only eligible rows are touched into:
   - DepartmentPublication
   - DepartmentScholarPublication
   - DepartmentRosterPublication

No year policy means unknown-year rows remain allowed.

The run summary now records:

PublicationRowsExcludedByYearPolicy=<count>

These exclusions do not make an otherwise complete identity/source refresh
partial. They are legitimate policy exclusions, not ingestion failures.

## OpenAlex fallback

The same local policy helper applies to OpenAlex fallback works.

OpenAlex API from/to filters are retained, but the local check is the final
correctness boundary.

## Reporting defense in depth

sql/447 updates academiconline.vw_DepartmentNewestPublication so the report
also enforces the persisted Department.RefreshFromYear / RefreshToYear policy.

When a department has a year policy, a NULL PublicationYear is never emitted by
the reporting view.

This protects the report immediately, including a previously promoted snapshot,
while the next worker refresh cleans the current snapshot attribution itself.

## Validation

After applying sql/447 and deploying the worker, run a normal Neurosurgery
refresh and then sql/448.

Expected:

- ScholarsDiscovered = 15
- ScholarsProcessed = 15
- IsCacheComplete = 1
- CurrentSnapshotRunId advances to the new run
- CurrentReportNullYears = 0
- CurrentReportInvalidYears = 0
- CurrentReportOutsideYearPolicy = 0
- CurrentDepartmentRowsOutsideYearPolicy = 0
- CurrentRosterRowsOutsideYearPolicy = 0
- global invalid numeric years remain 0
- global historical NULL years remain preserved for provenance

The exact PublicationsProcessed value may decline because rows outside the
department year policy are no longer counted as department publication rows.

## Files

- sql/447_V3E_PATCH021M_LocalPublicationYearPolicy_SQL2016.sql
- sql/448_V3E_PATCH021M_LocalPublicationYearPolicy_Validation_SQL2016.sql

## Deployment order

1. Pull PATCH021M.
2. Build the solution.
3. Stop the durable refresh service.
4. Run sql/447.
5. Deploy updated worker binaries.
6. Start the service.
7. Mark Department of Neurosurgery Due Now.
8. Restart the service for an immediate pass.
9. Run sql/448.
10. After PATCH021M validates, return to PATCH021L repository hygiene cleanup.
