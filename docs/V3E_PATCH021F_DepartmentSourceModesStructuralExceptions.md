# V3E PATCH021F — Department Source Modes + Structural Exceptions

## Goal

Support all 29 official UVA School of Medicine departments without pretending that Academic Online exposes the same organizational model for every department.

PATCH021D/PATCH021E established:

- 27 departments have usable direct Academic Online department nodes.
- Department of Medicine is represented by divisions rather than one reliable department node.
- Neurosurgery does not expose a usable Academic Online department node in the observed profiles.

PATCH021F makes those differences explicit.

## Source modes

`academiconline.Department.SourceMode` accepts:

- `DirectDepartment`
- `AggregateUnits`
- `FacultyRoster`

Existing departments default to `DirectDepartment`.

### Department of Medicine

Source mode:

`AggregateUnits`

Membership is assembled by querying the current official Department of Medicine divisions, unioning scholars by Academic Online author ID, and then passing the deduplicated scholar set into the existing publication/snapshot pipeline.

A division may have several Academic Online search aliases. The worker tries them in priority order and samples up to five returned profiles to require an affiliation matching the configured division before trusting the alias.

If any official division remains unresolved, the run is partial and the previous good snapshot is preserved.

### Department of Neurosurgery

Source mode:

`FacultyRoster`

The official UVA Neurosurgery faculty roster supplies membership. Academic Online supplies scholar identities and publications.

Resolved Academic Online author IDs are cached in `academiconline.DepartmentRosterMember`, reducing repeat identity-search calls on later weekly refreshes.

Identity resolution order:

1. exact published email;
2. normalized UVA userid with exact returned email;
3. strong first/last-name match;
4. exact normalized full-name tie-breaker.

If any active roster member remains unresolved, the run is partial and the previous good snapshot is preserved.

## Same downstream reporting pipeline

All source modes feed the existing tables:

- `academiconline.Scholar`
- `academiconline.ScholarDepartment`
- `academiconline.Publication`
- `academiconline.ScholarPublication`
- `academiconline.DepartmentPublication`
- `academiconline.DepartmentScholarPublication`

The existing snapshot promotion rule remains unchanged: only a completed, cache-complete run becomes current.

Therefore the existing `academiconline.vw_DepartmentNewestPublication` and manager-facing newest-publications page do not need a separate exception reporting implementation.

## SQL migration

Run:

`sql/431_V3E_PATCH021F_DepartmentSourceModes_SQL2016.sql`

Then inspect:

`sql/432_V3E_PATCH021F_DepartmentSourceModes_Diagnostics_SQL2016.sql`

Expected active catalog shape:

- 27 `DirectDepartment`
- 1 `AggregateUnits`
- 1 `FacultyRoster`
- 29 supported departments total

Medicine and Neurosurgery are inserted/updated as disabled catalog rows.

## Current source membership seeds

Medicine uses the current UVA Department of Medicine division roster.

Neurosurgery uses the current official UVA Neurosurgery faculty page.

These tables are intentionally editable local configuration. Future official roster changes can be applied without changing the sync algorithm.

## Deployment

1. Pull PATCH021F.
2. Run SQL 431.
3. Run SQL 432.
4. Build the solution.
5. Update the installed Windows Service binaries:

```powershell
.\scripts\Update-AcademicOnlineDepartmentRefreshServiceBinaries.ps1
```

6. Restart the web app.
7. Open:

`/Admin/AcademicOnlineDepartments`

The catalog now displays each department's source mode and source configuration count.

## First validation strategy

Do not enable both exception departments at once.

### Test Neurosurgery first

It is the smaller test.

In the admin catalog:

- Department of Neurosurgery
- leave From Year at 2025
- choose Verified Only according to the desired manager report policy
- Enable scheduled refresh
- Save Policy
- Mark Due Now

Then restart the service once for an immediate pass:

```powershell
Restart-Service OpenAlexAcademicOnlineDepartmentRefresh
```

After the run completes, execute:

`sql/433_V3E_PATCH021F_StructuralExceptionRefreshValidation_SQL2016.sql`

Inspect:

- resolved roster member count;
- unresolved names;
- cache completeness / snapshot promotion;
- newest publication rows.

Do not treat a partial first run as failure. The unresolved roster rows tell us exactly which identities need a precise UVA userid/email override.

### Test Medicine second

After Neurosurgery is understood, enable Department of Medicine.

The first aggregate run is expected to be more API-intensive because it must resolve the division filters and then load publications for the union of scholars.

Run SQL 433 afterward.

Inspect each official division's selected alias and any unresolved unit before accepting the snapshot.

## Safety rules

- New structural-exception departments are disabled by default.
- A noisy division alias is rejected unless sampled Academic Online profiles expose the configured division.
- Faculty-roster IDs are cached only after identity resolution.
- Incomplete membership never replaces a previously promoted snapshot.
- Publications continue to deduplicate through the existing Academic Online/OpenAlex/DOI/title-year logic.
