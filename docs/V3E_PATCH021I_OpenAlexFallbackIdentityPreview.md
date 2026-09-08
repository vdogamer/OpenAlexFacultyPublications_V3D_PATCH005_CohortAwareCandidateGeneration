# V3E PATCH021I — OpenAlex Fallback Identity Preview

## Evidence from PATCH021H

PATCH021H reduced the Neurosurgery unresolved roster from five to four by
recovering W. Jeffrey Elias through verified whole-cache Academic Online
coauthor evidence.

The four remaining roster members are:

- Aaron Dumont
- Paul Grabb
- Elisa Kucia
- Melike Mut Askun

The PATCH021H diagnostics show:

### Aaron Dumont

Academic Online scholar search returns zero candidates.

The whole-cache coauthor graph contains a non-null Academic Online coauthor
ID with 13 publications, but the UVA Academic Online author/general endpoint
returns no identity evidence for that ID. A direct author/publications request
also reports that the profile does not belong to the institution.

That ID must not be cached as Aaron's UVA Academic Online identity.

### Paul Grabb

Academic Online scholar search returns zero candidates.

The whole-cache coauthor graph contains no non-null Academic Online coauthor
ID matching the official roster name.

### Elisa Kucia

Academic Online scholar search returns zero candidates.

The whole-cache coauthor graph contains no non-null Academic Online coauthor
ID matching the official roster name.

### Melike Mut Askun

Academic Online scholar search returns zero candidates.

Name evidence exists in coauthor metadata, but no non-null Academic Online
coauthor ID is available.

## PATCH021I goal

PATCH021I adds a conservative OpenAlex fallback **preview** for unresolved
FacultyRoster members.

It does not yet make OpenAlex a publication source for the Department of
Neurosurgery.

It does not treat an OpenAlex proposal as cache-complete.

It does not promote a partial snapshot.

The goal is to determine whether each unresolved official roster member has
one defensible UVA-affiliated OpenAlex author identity before a later patch
introduces mixed-source publication retrieval.

## Identity rules

For each roster member still unresolved after Academic Online scholar search
and verified coauthor recovery, PATCH021I:

1. searches OpenAlex authors using at most four conservative name variants;
2. requests at most 25 authors per variant;
3. deduplicates candidates by OpenAlex author ID;
4. computes the existing conservative roster-name compatibility score;
5. requires NameScore >= 85;
6. requires UVA affiliation evidence from:
   - configured UVA OpenAlex institution IDs; or
   - University of Virginia / UVA Health institution names;
7. records a proposed OpenAlex author ID only when exactly one candidate
   passes the threshold;
8. records Ambiguous, NoMatch, or Unavailable otherwise.

No fuzzy surname-only acceptance is introduced.

## Database impact

PATCH021I adds four nullable fields to
academiconline.DepartmentRosterMember:

- OpenAlexAuthorId
- OpenAlexResolutionStatus
- OpenAlexResolutionEvidence
- OpenAlexLastResolvedAtUtc

These are separate from AcademicOnlineAuthorId.

A ProposedUnique OpenAlex identity does not change the PATCH021F/PATCH021H
completeness rule. The current snapshot still requires all active roster
members to have usable publication-source identities.

## SQL files

Apply:

sql/438_V3E_PATCH021I_OpenAlexFallbackIdentityPreview_SQL2016.sql

After the next Neurosurgery refresh, validate with:

sql/439_V3E_PATCH021I_OpenAlexFallbackIdentityPreview_Validation_SQL2016.sql

## Deployment

1. Pull PATCH021I.
2. Build the solution.
3. Apply SQL 438.
4. Update the installed durable worker binaries.
5. Leave Department of Medicine disabled.
6. Keep Department of Neurosurgery enabled.
7. Mark Neurosurgery Due Now.
8. Restart the Windows Service once for an immediate pass.
9. Run SQL 439 after the pass completes.

## Expected outcome

The expected result is not necessarily a complete Neurosurgery snapshot.

The desired PATCH021I output is an evidence table for the four unresolved
members showing one of:

- ProposedUnique
- Ambiguous
- NoMatch
- Unavailable

If OpenAlex returns ProposedUnique identities, the next patch can add
mixed-source publication retrieval in a controlled way.

If OpenAlex itself cannot distinguish one or more members, those identities
remain explicit manual-review exceptions rather than being silently guessed.
