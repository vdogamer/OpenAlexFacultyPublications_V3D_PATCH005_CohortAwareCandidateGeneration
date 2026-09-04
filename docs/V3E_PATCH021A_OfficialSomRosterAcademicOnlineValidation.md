# V3E PATCH021A — Official SOM Roster + Academic Online Validation

## Why PATCH021A exists

The first two PATCH021 discovery attempts established an important Academic Online API constraint.

An unfiltered scholar scan successfully returned 1,881 unique scholars across 20 pages, but the bulk scholar-search rows exposed no department affiliations. Therefore the system could not safely infer School of Medicine departments from that endpoint without fetching an individual profile for every scholar.

PATCH021A avoids that API-heavy design.

## Source-of-truth split

For the UVA School of Medicine:

- UVA School of Medicine's official public department roster supplies the candidate department names.
- Academic Online validates that each candidate department exists in its UVA scholar index and supplies the structured organization identity.
- Academic Online remains the source used for scholar/publication synchronization after a department is enabled.

The official School of Medicine structure used by this patch is:

- 20 clinical departments;
- 9 research departments;
- 29 departments total.

## SQL migration

After PATCH021 (421), run:

`sql/423_V3E_PATCH021A_OfficialDepartmentSeeds_SQL2016.sql`

Then validate the seed roster with:

`sql/424_V3E_PATCH021A_OfficialDepartmentSeeds_Diagnostics_SQL2016.sql`

Expected summary:

- Clinical official departments: 20
- Research official departments: 9

Multiple Academic Online search-name variants may exist for one official department.

Examples include ampersand versus 'and', and Genome Sciences versus Genomic Sciences.

## Validation algorithm

For each official department:

1. Try its Academic Online search-name variants in priority order.
2. Request only page 0 with at most five scholar rows.
3. If Academic Online returns a scholar, fetch one representative `author/general` profile.
4. Parse the profile's structured department hierarchy.
5. Require a direct child relationship under `School of Medicine`.
6. Require the returned department name to normalize to the official/search candidate.
7. If validated, onboard the structured Academic Online department ID, slug, parent ID/name/slug.
8. If no variant validates, leave that official unit unresolved and mark the overall discovery run `Partial`.

This is intentionally much lighter than profiling all 1,881 scholars.

## Safety

Newly validated departments remain:

`RefreshEnabled = 0`

No discovered department starts scheduled publication synchronization until an administrator reviews and explicitly enables it.

Existing Department of Cell Biology should be matched/enriched rather than duplicated.

## Deployment

After pulling PATCH021A:

1. Build the solution.
2. Run SQL 423.
3. Run SQL 424.
4. Update the installed durable service binaries:

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

## Expected result

The new request should normally be `Completed` if all 29 official departments validate.

A `Partial` result is acceptable during validation and means one or more official departments did not resolve to an Academic Online structured department. The successfully validated departments are still onboarded.

The diagnostic SummaryText reports:

- search requests;
- representative profiles fetched;
- profile validation failures;
- unresolved departments;
- departments discovered;
- departments inserted;
- departments updated.

A `Failed` result should now indicate a genuine request-level problem rather than absence of department metadata in the bulk scholar endpoint.

## Audit history

Failed discovery runs 1 and 2 should remain in the database. They document why the discovery mechanism changed and should not be deleted merely to make the history look clean.
