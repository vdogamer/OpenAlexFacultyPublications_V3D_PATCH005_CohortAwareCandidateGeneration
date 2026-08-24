# V3E PATCH017B8 - Five-Faculty Cohort Expansion Preflight

## Goal

Scale the proven single-faculty path to a controlled five-faculty cohort without changing matching behavior or writing any cohort/matching data during selection.

The successful FacultyId 16 path proved the end-to-end sequence through controlled publication-candidate creation. PATCH017B8 now asks a narrower scaling question:

> Which five active faculty should be the next cohort, and are they suitable for a controlled multi-faculty run?

## Why five faculty

Five is large enough to expose cross-faculty identity variation, name ambiguity, missing ORCID/claimed-ID cases, and review-volume issues while remaining small enough for manual inspection and rollback-oriented troubleshooting.

## Existing cohort capability

`CandidateGenerationRepository.LoadFacultyForPilotCohortAsync` already supports selecting enabled faculty from `openalex.PilotFacultyCohort` by `CohortName`, ordered by `PilotOrder`.

`FacultyIdentityCandidateGenerationJob` already switches from the full active faculty list to this cohort-specific loader when `CandidateGeneration:PilotCohortName` is supplied.

PATCH017B8 therefore does not rewrite the matching engine. It adds a safe cohort-selection stage before registration.

## Preflight script

Run:

`sql/385_V3E_PATCH017B8_FiveFacultyCohortExpansion_Preflight_SQL2016.sql`

The script is read-only. It:

1. Uses FacultyId 16 as the proven anchor.
2. Detects the anchor's department from `openalex.Faculty`.
3. Reports whether `openalex.PilotFacultyCohort` exists and shows its column shape.
4. Builds a readiness profile for active faculty excluding FacultyId 16.
5. Prefers faculty in the same department.
6. Prefers faculty not already enabled in another pilot cohort.
7. Prefers faculty with a fresh identity pipeline so the next cohort exercises identity discovery/review rather than only reusing earlier accepted decisions.
8. Ranks identity seed strength as:
   - ORCID + claimed OpenAlex author ID
   - ORCID only
   - claimed OpenAlex author ID only
   - name only
9. Prefers faculty without existing current publication decisions so the cohort can exercise the complete publication-review path.
10. Returns a wider top-20 pool and a deterministic suggested five-person cohort.

## Safety guarantee

PATCH017B8 preflight performs no INSERT, UPDATE, DELETE, MERGE, API call, identity decision, publication candidate, or publication decision operation.

Expected safety text:

`PreviewOnly_NoCohortOrMatchingDataChanged`

## Expected next step

Review the five rows returned by result set 4. Do not register the cohort until the five FacultyIds/names and the reported `PilotFacultyCohort` schema have been inspected.

After validation, PATCH017B8 will add a controlled registration script that writes only those five faculty into a named enabled pilot cohort:

`V3E_PATCH017B8_FIVE_FACULTY`

The first cohort job after registration will be the existing identity-discovery job with `CandidateGeneration:PilotCohortName` set to that exact value and a maximum of five faculty.

## Scale gate

Do not expand to a department-wide run until the five-faculty cohort has completed these gates:

- identity candidate generation completed for all five;
- identity summaries reviewed with zero unresolved accidental cross-person matches;
- accepted identities bridged successfully;
- publication candidate generation/preview shows valid per-work authorship evidence;
- publication review UI remains manageable;
- accepted-publication and provenance reports remain internally consistent;
- no duplicate-candidate or stale-decision behavior appears across the cohort.
