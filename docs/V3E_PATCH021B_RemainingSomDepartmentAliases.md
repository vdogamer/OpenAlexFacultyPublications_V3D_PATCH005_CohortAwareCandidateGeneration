# V3E PATCH021B — Resolve Remaining SOM Department Aliases

## Starting point

PATCH021A validated 24 of the 29 official UVA School of Medicine departments.

The five unresolved official departments were:

- Biomedical Engineering
- Medicine
- Neurosurgery
- Orthopaedic Surgery
- Otolaryngology

PATCH021B targets only those five. It does not change the 24 departments that already validated successfully.

## Why these five need special handling

Four are likely Academic Online naming differences.

PATCH021B adds targeted validation aliases:

- Medicine → Internal Medicine
- Neurosurgery → Neurological Surgery
- Orthopaedic Surgery → Orthopedic Surgery / Orthopedics
- Otolaryngology → Otolaryngology-Head and Neck Surgery

Biomedical Engineering is structurally different. UVA identifies Biomedical Engineering as a joint department of the School of Medicine and UVA Engineering.

PATCH021B therefore allows a BME representative profile to validate under either:

- School of Medicine, or
- School of Engineering and Applied Science.

The actual Academic Online department and parent identity returned by the representative profile are retained in the local catalog.

## Migration

Run after PATCH021A:

`sql/425_V3E_PATCH021B_RemainingDepartmentAliases_SQL2016.sql`

Then inspect:

`sql/426_V3E_PATCH021B_RemainingDepartmentAliases_Diagnostics_SQL2016.sql`

The migration is idempotent.

Existing Biomedical Engineering seed rows are updated with the alternate validation parent; duplicate BME search rows are not inserted.

## Worker changes

The discovery seed model now carries a per-seed `ValidationParentName`.

During representative-profile validation, the worker accepts a direct department child under either:

1. the discovery request parent; or
2. the seed-specific alternate validation parent.

For ordinary School of Medicine departments those values are the same.

For Biomedical Engineering the alternate parent is UVA Engineering.

## Deployment

After pulling PATCH021B:

1. Build the solution.
2. Run SQL 425.
3. Run SQL 426.
4. Update the installed durable Windows Service binaries:

```powershell
.\scripts\Update-AcademicOnlineDepartmentRefreshServiceBinaries.ps1
```

5. Restart the web application.
6. Queue a new School of Medicine discovery request.
7. For immediate processing:

```powershell
Restart-Service OpenAlexAcademicOnlineDepartmentRefresh
```

8. Run:

`sql/422_V3E_PATCH021_DepartmentDiscovery_Diagnostics_SQL2016.sql`

## Target result

The ideal discovery run is:

- `RunStatus = Completed`
- `DepartmentsDiscovered = 29`
- `DepartmentsUnresolved = 0`

Because 24 departments are already in the catalog, the new run should primarily update existing rows and insert only the departments that were previously unresolved.

A `Partial` result remains safe. It means one or more of the targeted aliases still did not validate. Successfully validated departments remain onboarded and newly inserted rows remain `RefreshEnabled = 0`.

## Safety rule

PATCH021B does not relax identity validation globally.

Only Biomedical Engineering receives an alternate-parent rule, based on its documented joint School of Medicine / Engineering status.

The other unresolved departments still must validate as direct School of Medicine child units.
