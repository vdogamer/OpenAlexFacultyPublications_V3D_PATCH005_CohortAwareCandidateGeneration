# V3E PATCH001 - Faculty Identity Candidate Discovery

## Scope

This patch creates the first real disambiguation layer.

It does **not** accept, reject, merge, or publish author identities. It only discovers possible OpenAlex author identities and stores them for review.

## New worker job

```text
FacultyIdentityCandidateGeneration
```

The job honors:

```json
"CandidateGeneration": {
  "PilotCohortName": "Pilot Expansion 15 Faculty",
  "MaxFacultyToProcess": 15,
  "MaxIdentityAuthorSearchResults": 25
}
```

When `PilotCohortName` is configured, it processes enabled active faculty from:

```text
openalex.PilotFacultyCohort
```

## New table

Run this first:

```sql
sql/200_V3E_PATCH001_FacultyIdentityCandidate.sql
```

It creates:

```text
openalex.FacultyIdentityCandidate
```

Important fields:

```text
FacultyId
CandidateAuthorOpenAlexId
CandidateAuthorOpenAlexShortId
CandidateDisplayName
CandidateOrcid
WorksCount
CitedByCount
LastKnownInstitutionsJson
DiscoveryMethod
EvidenceScore
EvidenceStatus
EvidenceSummary
EvidenceJson
IngestionRunId
```

## Discovery methods

The first patch uses conservative discovery methods:

```text
ClaimedOpenAlexAuthorId
OrcidLookup
ExactFullNameSearch
FirstLastNameSearch
FirstMiddleLastNameSearch
FirstInitialLastNameSearch
```

Each method creates a reviewable evidence row. The same OpenAlex author can appear under multiple discovery methods because this helps diagnose which search paths are useful.

## Evidence statuses

Rows are marked as one of:

```text
StrongIdentifierCandidate
CandidateForReview
PossibleNameCandidate
WeakNameCandidate
NeedsMoreEvidence
```

These are not acceptance statuses. They are evidence-quality labels.

## How to test locally

From the repo root:

```powershell
git checkout v3e-patch001-identity-candidates
```

Run the SQL table script in SSMS:

```sql
sql/200_V3E_PATCH001_FacultyIdentityCandidate.sql
```

Build:

```powershell
dotnet build OpenAlexFacultyPublications.sln
```

Run the worker with:

```json
"Worker": {
  "Enabled": true,
  "JobName": "FacultyIdentityCandidateGeneration",
  "RunOnceAndStop": true,
  "DelaySeconds": 10
}
```

Then run diagnostics:

```sql
sql/201_V3E_PATCH001_IdentityCandidate_Diagnostics_SQL2016.sql
```

## What success looks like

For the pilot cohort, diagnostics should show:

```text
SelectedCohortName = Pilot Expansion 15 Faculty
EnabledActiveCohortFacultyCount = 15
Identity candidate rows grouped by FacultyId
CandidateCountsByFaculty in the run summary
```

For Susan M. Pollart, the table should surface multiple possible OpenAlex author identities if OpenAlex returns them through the search methods.

## What this patch intentionally does not do

This patch does not:

```text
auto-accept OpenAlex author IDs
merge split OpenAlex author profiles
reject false positives
generate publications from identity candidates
change accepted author decisions
change publication candidate generation
create a review UI
```

Those are later V3E patches.
