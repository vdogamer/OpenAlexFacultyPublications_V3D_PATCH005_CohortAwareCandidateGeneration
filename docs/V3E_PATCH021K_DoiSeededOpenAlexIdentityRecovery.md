# V3E PATCH021K — DOI-Seeded OpenAlex Identity Recovery

## Starting point

PATCH021J successfully validated mixed-source FacultyRoster publication retrieval for
Department of Neurosurgery:

- 11 roster members use Academic Online.
- Aaron Dumont uses OpenAlex fallback A5059146591.
- Paul Grabb uses OpenAlex fallback A5057859273.
- Melike Mut Askun uses OpenAlex fallback A5130681923.
- Elisa Kucia remains the only unresolved official roster member.

The run correctly remained partial at 14/15.

## Goal

Recover Elisa Kucia's OpenAlex author identity without weakening general author-name
matching.

PATCH021K uses independently curated publication seeds instead of broader fuzzy search.

## Seed evidence

PATCH021K creates an auditable
academiconline.DepartmentRosterIdentitySeed table.

The initial Elisa Kucia seeds are:

1. DOI 10.1227/NEU.0b013e3181fdf912
   - OpenAlex work W2065806146
   - PubMed PMID 21099714

2. DOI 10.1227/NEU.0b013e318208f181
   - OpenAlex work W2021763973
   - PubMed PMID 21206303

3. DOI 10.1016/j.wneu.2011.06.008
   - OpenAlex work W1990697343
   - PubMed PMID 22120367

SQL does not assign an author ID.

The worker must derive the author ID independently from the authorships on the seeded
OpenAlex works.

## Recovery rules

For an unresolved official roster member with at least two active DOI seeds:

1. fetch each configured OpenAlex work by its OpenAlex work ID;
2. verify that the work DOI matches the configured DOI seed;
3. inspect the work authorships;
4. require exactly one author on that work with roster-name compatibility >= 85;
5. collect the matched OpenAlex author ID;
6. require at least two independently matched works;
7. reject the recovery if the matched works point to more than one author ID;
8. fetch the converged OpenAlex author entity directly;
9. require direct author-name compatibility >= 85;
10. accept as DoiSeedConverged when either:
    - the author entity has UVA affiliation evidence; or
    - all three independent DOI seeds converge on the same author and the official
      FacultyRoster supplies current UVA membership.

A single DOI is never sufficient.

A DOI/OpenAlex-work mismatch is rejected.

Conflicting author IDs are rejected.

## OpenAlex fallback semantics

DoiSeedConverged is treated as a usable OpenAlex fallback identity alongside
ProposedUnique.

It remains separate from AcademicOnlineAuthorId.

The existing PATCH021J mixed-source publication path retrieves OpenAlex works for the
resolved author using the configured department year window.

OpenAlex-only publications continue to use Verified = NULL.

VerifiedOnly=true still prevents OpenAlex fallback identities from satisfying the
department completeness rule.

## Expected result

If the three Elisa Kucia DOI seeds converge as expected, the next Neurosurgery run
should have:

- ScholarsDiscovered = 15
- ScholarsProcessed = 15
- IsCacheComplete = 1
- CurrentSnapshotRunId advances to the new run
- Department of Neurosurgery becomes available through the current-snapshot reporting
  view

If the DOI seeds do not converge, the run remains partial and
OpenAlexResolutionEvidence records the exact reason.

## SQL files

Apply before deploying the PATCH021K worker:

sql/443_V3E_PATCH021K_DoiSeededIdentityRecovery_SQL2016.sql

After the next Neurosurgery refresh, run:

sql/444_V3E_PATCH021K_DoiSeededIdentityRecovery_Validation_SQL2016.sql

## Deployment order

1. Pull PATCH021K.
2. Build the solution.
3. Stop the durable refresh service.
4. Run SQL 443.
5. Deploy the updated worker binaries.
6. Start the service.
7. Mark Neurosurgery Due Now.
8. Restart the service for an immediate pass.
9. Run SQL 444 after the pass completes.
