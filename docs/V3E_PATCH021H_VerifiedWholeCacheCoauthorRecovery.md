# V3E PATCH021H — Verified Whole-Cache Coauthor Identity Recovery

## Starting point

PATCH021G proved that five official Neurosurgery roster members are not surfaced by the Academic Online scholar-search endpoint:

- Aaron Dumont
- W. Jeffrey Elias
- Paul Grabb
- Elisa Kucia
- Melike Mut Askun

The local publication/coauthor graph produced stronger evidence:

- W. Jeffrey Elias: non-null coauthor author ID found; UVA author/general validates William Elias, wje4r@virginia.edu, University of Virginia, Professor of Neurological Surgery.
- Aaron Dumont: non-null coauthor author ID found, but the UVA-scoped author/general payload contains no usable identity evidence and author/publications rejects the profile as not belonging to the institution.
- Melike Mut Askun: coauthor-name evidence exists but the observed coauthor ID is null.
- Paul Grabb and Elisa Kucia: no usable coauthor author ID had been found in the initial Neurosurgery-run scan.

PATCH021H therefore does not trust coauthor IDs by name alone.

## Goal

Recover only coauthor-derived identities that can be independently validated against the UVA Academic Online profile endpoint.

## Recovery order

For an unresolved FacultyRoster member:

1. run the existing exact email/userid/name scholar-search resolver;
2. if unresolved, query the entire local Academic Online publication cache for coauthor names using bounded token pairs;
3. keep only non-null Academic Online author IDs;
4. call UVA-scoped author/general for at most 10 candidate IDs;
5. require:
   - strong roster-name compatibility; and
   - UVA identity evidence from email, institution, or School of Medicine affiliation;
6. prefer exact expected-email evidence, then a clear profile-evidence score;
7. cache an author ID only when one candidate remains defensible.

An empty or non-UVA profile is rejected even if many publications contain a matching coauthor name.

## Name matching

PATCH021H expands one conservative case needed by the evidence:

- roster: W. Jeffrey Elias
- profile/coauthor: William Jeffrey Elias / William Elias

A candidate can receive a strong name score when the surname matches and at least two significant tokens overlap. Arbitrary fuzzy surname matching is not used.

## Resolution audit

PATCH021H adds:

- ResolutionMethod
- ResolutionEvidence

to academiconline.DepartmentRosterMember.

Existing cached identities are labeled as pre-PATCH021H cached identities.

New successful coauthor recovery records the method as VerifiedCoauthorCache together with the profile evidence used.

Unresolved attempts retain detailed evidence explaining why a coauthor ID was rejected or why no non-null ID was found.

## SQL files

Before deployment:

1. Run the read-only diagnostic:
   sql/435_V3E_PATCH021H_WholeCacheCoauthorIdentity_Diagnostics_SQL2016.sql

2. Apply the audit-column migration:
   sql/436_V3E_PATCH021H_VerifiedCoauthorRecovery_SQL2016.sql

After the next Neurosurgery refresh:

3. Run:
   sql/437_V3E_PATCH021H_VerifiedCoauthorRecovery_Validation_SQL2016.sql

## Deployment

1. Pull PATCH021H.
2. Build the solution.
3. Run SQL 435.
4. Run SQL 436.
5. Update the Windows Service binaries:

```powershell
.\scripts\Update-AcademicOnlineDepartmentRefreshServiceBinaries.ps1
```

6. Leave Department of Medicine disabled.
7. Keep Neurosurgery enabled.
8. Mark Neurosurgery Due Now.
9. Restart the service once for an immediate pass:

```powershell
Restart-Service OpenAlexAcademicOnlineDepartmentRefresh
```

10. Run SQL 437 after the refresh completes.

## Expected immediate outcome

W. Jeffrey Elias should be a strong candidate for automatic recovery.

Aaron Dumont should remain unresolved unless a UVA-valid profile becomes available; the known non-institution coauthor ID must not be cached.

Paul Grabb, Elisa Kucia, and Melike Mut Askun remain evidence-driven: they resolve only if the whole local cache contains a non-null coauthor ID whose UVA profile validates.

Snapshot-promotion safety remains unchanged. No partial Neurosurgery snapshot is promoted until every active roster member is resolved and processed.
