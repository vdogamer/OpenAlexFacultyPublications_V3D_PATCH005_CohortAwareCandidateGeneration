# V3E PATCH021D — Targeted Unresolved Department Diagnostics

## Goal

PATCH021C still validated 27 of the 29 official School of Medicine departments.

The remaining unresolved departments are:

- Medicine
- Neurosurgery

PATCH021D does not add aliases or relax matching rules.

Instead, it records the exact Academic Online profile affiliation structures observed for unresolved departments so the next change can be based on evidence.

## What gets logged

Only unresolved official departments produce diagnostic log rows.

For each attempted alias, PATCH021D records:

- alias searched;
- validation parent;
- returned candidate author IDs;
- each candidate profile's parsed department name;
- parent name;
- department slug;
- parent slug;
- profile request error or timeout, if any.

No email address or API key is logged.

## Deployment

No SQL migration is required.

After pulling PATCH021D:

1. Build the solution.
2. Update the installed Windows Service binaries:

```powershell
.\scripts\Update-AcademicOnlineDepartmentRefreshServiceBinaries.ps1
```

3. Queue another School of Medicine discovery request.
4. For immediate processing:

```powershell
Restart-Service OpenAlexAcademicOnlineDepartmentRefresh
```

5. Wait for the request to complete.
6. Run:

`sql/427_V3E_PATCH021D_UnresolvedDepartmentDiagnostics_SQL2016.sql`

## Expected result

Normally there should be two latest warning rows:

- Medicine
- Neurosurgery

Their Message values should reveal what Academic Online actually returned for the candidate profiles.

That evidence should be used to build the next resolver change. Do not add more aliases until those messages are inspected.
