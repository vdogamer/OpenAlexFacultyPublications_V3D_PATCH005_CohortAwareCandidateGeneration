# V3E PATCH019A — Department Cache Completeness + Title Cleanup

## Goal

Make the PATCH019 department report explicit about whether its local Academic Online cache is complete for the latest sync scope, and clean lightweight source HTML markup from publication titles before display.

## Database migration

After PATCH019 has already been installed, run:

`sql/413_V3E_PATCH019A_DepartmentCacheCompleteness_SQL2016.sql`

Then use:

`sql/414_V3E_PATCH019A_DepartmentCacheCompleteness_Diagnostics_SQL2016.sql`

PATCH019A adds these run-level fields to `academiconline.SyncRun`:

- `ScholarsDiscovered`
- `MaxScholarsToProcess`
- `ScholarPaginationSafeguardHit`
- `PublicationPaginationSafeguardHitCount`
- `IsCacheComplete`

Existing PATCH019 runs remain valid. Their completeness is shown as UNKNOWN until a PATCH019A sync is performed.

## Completeness definition

A completed department sync is COMPLETE only when all of the following are true:

1. every scholar discovered by the department search was processed;
2. the scholar-pagination safeguard was not hit;
3. no processed scholar's publication retrieval hit the publication-pagination safeguard.

Completeness applies to the sync scope. For example:

`2025–present; all returned publications`

is different from a full historical cache.

If `MaxScholarsToProcess` is lower than the number of discovered scholars, the cache is PARTIAL.

If scholar pagination itself reaches its safeguard, the cache is PARTIAL and `ScholarsDiscovered` should be treated as a lower bound.

## Manager-facing report

`/Reports/DepartmentPublications`

The department summary now displays:

- synchronized scholars versus discovered scholars;
- unique local publication count;
- COMPLETE / PARTIAL / UNKNOWN cache status;
- latest sync scope;
- latest sync-run ID;
- reasons for a PARTIAL cache;
- last completed cache refresh time.

## Publication-title cleanup

Academic Online can return lightweight HTML markup in titles, for example:

`<i>Toxoplasma gondii</i> effector...`

PATCH019A uses `AcademicOnlineTextCleaner` to strip source markup, HTML-decode entities, and normalize whitespace before manager-facing display.

The original Academic Online raw JSON remains preserved in the reporting store.

## Cell Biology validation

After applying the migration and pulling PATCH019A, a safe completeness test is:

```powershell
.\scripts\Set-AcademicOnlineApiKeyForCurrentSession.ps1

.\scripts\Run-AcademicOnlineDepartmentSync.ps1 `
    -DepartmentName "Department of Cell Biology" `
    -FromYear 2025 `
    -MaxScholars 10
```

If Academic Online discovers more than 10 Cell Biology scholars, the report should show PARTIAL and display:

`10 of N discovered scholar(s) synchronized`

A subsequent run with a high enough scholar cap should become COMPLETE provided neither pagination safeguard is reached.
