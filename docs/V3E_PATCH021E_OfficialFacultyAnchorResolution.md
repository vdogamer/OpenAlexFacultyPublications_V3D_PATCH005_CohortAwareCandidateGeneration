# V3E PATCH021E — Official Faculty Anchor Resolution

## Evidence from PATCH021D

The remaining unresolved departments were:

- Medicine
- Neurosurgery

PATCH021D showed that Academic Online's department filter was not trustworthy enough for these two cases.

For Medicine, the broad `Medicine` filter returned candidate profiles affiliated with other School of Medicine units such as Radiation Oncology, Pharmacology, Microbiology/Immunology/Cancer Biology, Perceptual Studies, and Surgery.

For Neurosurgery, all current department aliases returned no usable candidates.

PATCH021E therefore does not add another layer of department-name guessing.

## Strategy

PATCH021E introduces a small official-faculty anchor table.

For an unresolved department only:

1. Resolve an official UVA faculty anchor by exact published email when available, otherwise by a strong first/last-name match.
2. Fetch the anchor's Academic Online profile.
3. Parse structured affiliations.
4. Require a direct child organizational unit under School of Medicine.
5. Require that returned child to match the existing official department/alias set.
6. Onboard the Academic Online department identity if validation succeeds.

The normal department alias resolver still runs first. Anchors are a bounded fallback only.

## Current official anchors

Medicine:

- Taison Bell — current Department of Medicine interim chair.
- Matthew Reilley — current UVA School of Medicine Faculty Senate representative identified with Department of Medicine.

Neurosurgery:

- Aaron S. Dumont — current chair of Neurosurgery, with published UVA email/computing ID `asd2f@virginia.edu`.
- Ashok R. Asthagiri — Professor of Neurosurgery and department vice-chair.

## Migration

Run:

`sql/428_V3E_PATCH021E_OfficialFacultyAnchors_SQL2016.sql`

Then inspect:

`sql/429_V3E_PATCH021E_OfficialFacultyAnchors_Diagnostics_SQL2016.sql`

## Deployment

After pulling PATCH021E:

1. Build the solution.
2. Run SQL 428.
3. Run SQL 429.
4. Update the installed durable worker binaries:

```powershell
.\scripts\Update-AcademicOnlineDepartmentRefreshServiceBinaries.ps1
```

5. Restart the web application if needed.
6. Queue a new School of Medicine discovery request.
7. For immediate processing:

```powershell
Restart-Service OpenAlexAcademicOnlineDepartmentRefresh
```

8. Wait for the request to finish.
9. Run:

`sql/430_V3E_PATCH021E_AnchorResolutionValidation_SQL2016.sql`

## Target result

Ideal:

- `RunStatus = Completed`
- `DepartmentsDiscovered = 29`
- Medicine appears in `academiconline.Department`
- Neurosurgery appears in `academiconline.Department`
- newly inserted departments remain `RefreshEnabled = 0`

If one remains unresolved, PATCH021D diagnostics still capture the anchor attempt and returned affiliation structure, so the next change can remain evidence-based.
