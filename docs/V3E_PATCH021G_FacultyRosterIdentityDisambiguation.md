# V3E PATCH021G — Faculty-Roster Identity Disambiguation

## Starting point

The first PATCH021F Neurosurgery refresh resolved 10 of 15 official roster members.

Five remained unresolved:

- Aaron Dumont
- W. Jeffrey Elias
- Paul Grabb
- Elisa Kucia
- Melike Mut Askun

The refresh correctly remained partial and did not promote an incomplete snapshot.

## Goal

Resolve ambiguous or variant Academic Online scholar identities without weakening the identity-safety rule.

PATCH021G does not hard-code Academic Online author IDs.

It improves the bounded resolver and caches an author ID only after a unique identity is defensible.

## Resolution flow

Previously, FacultyRoster resolution relied primarily on search-result name/email fields.

PATCH021G now:

1. keeps exact expected UVA email and userid searches;
2. searches the official roster display name;
3. adds bounded name variants:
   - initials removed;
   - first + last;
   - first + second significant token for multi-part names;
4. deduplicates candidates by Academic Online author ID;
5. evaluates at most 10 name-compatible candidate profiles;
6. scores profile identity evidence using:
   - exact expected email;
   - strong name compatibility;
   - UVA email;
   - University of Virginia / UVA Health institution;
   - School of Medicine affiliation;
   - Neurosurgery / Neurological Surgery affiliation;
7. accepts only one defensible candidate, or a top candidate with a clear evidence margin;
8. records detailed candidate evidence in ResolutionError when ambiguity remains.

## Name variants

The name scorer intentionally handles examples such as:

- W. Jeffrey Elias -> Jeffrey Elias
- Aaron S. Dumont -> Aaron Dumont
- Melike Mut Askun -> Melike Mut / Melike Askun

It does not accept arbitrary fuzzy surname similarity.

At least two significant name tokens and UVA identity evidence are still required for profile-based resolution.

## API load

Ten previously resolved roster members already have cached Academic Online IDs, so they bypass search.

Only unresolved members invoke the enhanced search/profile-validation path.

Once a remaining identity is resolved, its author ID is cached for later scheduled refreshes.

## Snapshot safety

The PATCH021F promotion rule is unchanged.

A Neurosurgery run is cache-complete only when:

- every active roster member resolves;
- every selected scholar is processed;
- no publication pagination safeguard is hit.

Until then, the run remains partial and no incomplete current snapshot is promoted.

## Deployment

No database migration is required.

1. Pull PATCH021G.
2. Build the solution.
3. Update the installed Windows Service binaries:

```powershell
.\scripts\Update-AcademicOnlineDepartmentRefreshServiceBinaries.ps1
```

4. Leave Department of Medicine disabled.
5. Department of Neurosurgery may remain enabled.
6. Mark Neurosurgery Due Now in the admin catalog.
7. Restart the service once for an immediate pass:

```powershell
Restart-Service OpenAlexAcademicOnlineDepartmentRefresh
```

8. After the run completes, execute:

`sql/434_V3E_PATCH021G_FacultyRosterIdentityDiagnostics_SQL2016.sql`

## Target

Ideal result:

- 15 total roster members
- 15 resolved roster members
- 0 unresolved roster members
- run status Completed
- IsCacheComplete = 1
- CurrentSnapshotRunId advances
- newest Neurosurgery publications become available through the existing reporting view/page

If one or more identities remain unresolved, SQL 434 exposes the profile evidence used for each decision so only precise overrides need to be considered.
