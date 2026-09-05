# V3E PATCH021C — Multi-Candidate Department Validation

## Goal

Resolve the final two School of Medicine departments that remained unresolved after PATCH021B:

- Medicine
- Neurosurgery

PATCH021C does not add more aliases.

Instead, it corrects a resolver weakness in PATCH021B.

## Resolver flaw

PATCH021B searched Academic Online with an alias and requested up to five scholar candidates, but it validated only the first returned scholar profile.

For broad department terms such as Medicine and Neurosurgery, the first-ranked scholar can be a poor representative even when another returned candidate belongs to the correct structured department.

## PATCH021C behavior

For each department alias:

1. Search Academic Online page 0 with at most five scholar candidates.
2. Preserve the bounded five-result limit.
3. Fetch candidate profiles one at a time.
4. Validate each profile against:
   - the allowed parent hierarchy;
   - the department alias;
   - the official department name.
5. Stop at the first valid structured department match.
6. If none of the five validate, continue to the next alias.

The existing Biomedical Engineering alternate-parent rule remains unchanged.

## API-load impact

This increases profile-validation calls only when the first candidate does not validate.

The upper bound remains small:

- at most five candidate profiles per alias;
- page 0 only;
- no bulk profiling of all Academic Online scholars.

## Migration

No SQL migration is required for PATCH021C.

PATCH021B seed rows and validation-parent rules remain valid.

## Deployment

After pulling PATCH021C:

1. Build the solution.
2. Update the installed durable service binaries:

```powershell
.\scripts\Update-AcademicOnlineDepartmentRefreshServiceBinaries.ps1
```

3. Restart the web application if needed.
4. Queue another School of Medicine discovery request.
5. For immediate processing:

```powershell
Restart-Service OpenAlexAcademicOnlineDepartmentRefresh
```

6. Run:

`sql/422_V3E_PATCH021_DepartmentDiscovery_Diagnostics_SQL2016.sql`

## Target result

Ideal:

- `RunStatus = Completed`
- `DepartmentsDiscovered = 29`
- `DepartmentsUnresolved = 0`

Because 27 departments are already onboarded, a successful run should mostly update existing rows and insert only the final missing departments.

A `Partial` result remains safe and means one or both departments still require a more specific Academic Online identity investigation.
