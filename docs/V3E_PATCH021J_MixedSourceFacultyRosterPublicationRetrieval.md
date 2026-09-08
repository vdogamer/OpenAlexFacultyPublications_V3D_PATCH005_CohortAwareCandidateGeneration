# V3E PATCH021J — Mixed-Source Faculty-Roster Publication Retrieval

## Starting point

PATCH021I established the following Department of Neurosurgery identity state:

- 11 official roster members have Academic Online identities.
- Aaron Dumont has ProposedUnique OpenAlex author A5059146591.
- Paul Grabb has ProposedUnique OpenAlex author A5057859273.
- Melike Mut Askun has ProposedUnique OpenAlex author A5130681923.
- Elisa Kucia remains NoMatch.

PATCH021I deliberately did not use the OpenAlex identities for publication retrieval.

## Goal

PATCH021J allows an official FacultyRoster department to retrieve publications from
more than one provider without corrupting provider-specific identity semantics.

AcademicOnlineAuthorId remains an Academic Online identity.

OpenAlexAuthorId remains an OpenAlex identity.

Neither is copied into the other provider's column.

## Retrieval model

For each official roster member:

1. If AcademicOnlineAuthorId is available, use the existing Academic Online
   publication retrieval path.
2. Otherwise, if OpenAlexResolutionStatus = ProposedUnique and an
   OpenAlexAuthorId is available, use OpenAlex works for that author.
3. Otherwise, the roster member remains unresolved.

For the current Neurosurgery pilot this means:

- 11 Academic Online publication sources
- 3 OpenAlex fallback publication sources
- 1 unresolved member: Elisa Kucia

## OpenAlex query scope

OpenAlex fallback retrieval uses the author ID plus the department refresh year
window.

The worker also applies the year bounds locally before persistence.

The existing MaxPublicationPages safeguard applies to OpenAlex fallback pages.

## VerifiedOnly semantics

Academic Online VerifiedOnly is provider-specific.

OpenAlex does not provide the same verification concept.

Therefore:

- OpenAlex-only publication rows are inserted with Verified = NULL.
- A FacultyRoster refresh configured with VerifiedOnly = true does not treat
  OpenAlex fallback identities as usable publication sources.
- Such a run remains incomplete rather than silently weakening the policy.

## Publication deduplication

OpenAlex fallback publications enter the existing academiconline.Publication
store through the established identity order:

- Academic Online publication ID
- OpenAlex work ID
- normalized DOI
- normalized title + year fingerprint

This lets an OpenAlex fallback work merge with an already-known Academic Online
publication rather than create a duplicate.

If an OpenAlex fallback touches a publication that already has an Academic Online
publication ID, the existing Academic Online RawJson payload is preserved.

## Roster attribution

PATCH021J adds:

academiconline.DepartmentRosterPublication

The table connects an official roster member to a publication and records:

- DepartmentId
- DepartmentRosterMemberId
- PublicationKey
- SourceProvider
- SourceAuthorId
- LastSyncRunId
- IsCurrent
- CurrentSnapshotRunId

SourceProvider is constrained to:

- AcademicOnline
- OpenAlex

Academic Online FacultyRoster publications are also written to this table so
FacultyRoster reporting no longer depends on pretending every roster identity is
an Academic Online scholar.

## Reporting view

academiconline.vw_DepartmentNewestPublication is updated so:

- DirectDepartment and AggregateUnits continue using
  DepartmentScholarPublication + Scholar.
- FacultyRoster uses DepartmentRosterPublication + DepartmentRosterMember.

This makes FacultyNames and FacultyCount provider-neutral for roster-driven
departments.

## Snapshot safety

The existing safety gate remains unchanged in principle.

A FacultyRoster snapshot can be promoted only when:

- every active official roster member has a usable publication-source identity;
- the number of processed roster identities equals ScholarsDiscovered;
- no scholar/source membership safeguard is hit;
- no publication pagination safeguard is hit.

The current Neurosurgery run should therefore remain partial because Elisa Kucia
is still unresolved.

PATCH021J is intended to validate mixed-source retrieval for the other 14 roster
members without exposing an incomplete department report.

## SQL files

Apply before deploying the worker:

sql/440_V3E_PATCH021J_MixedSourceRosterPublication_SQL2016.sql

After the next Neurosurgery refresh, run:

sql/441_V3E_PATCH021J_MixedSourceRosterPublication_Validation_SQL2016.sql

## Expected validation

The first PATCH021J run should show approximately:

- ScholarsDiscovered = 15
- ScholarsProcessed = 14
- IsCacheComplete = 0
- CurrentSnapshotRunId remains NULL

DepartmentRosterPublication should contain latest-run rows from both:

- AcademicOnline
- OpenAlex

The three OpenAlex fallback roster members should have publication attribution
rows if OpenAlex returns works in the configured year window.

OpenAlex-only rows should retain Verified = NULL.

The next patch can focus exclusively on Elisa Kucia and the final completeness
decision rather than revisiting the 14 identities already established.
