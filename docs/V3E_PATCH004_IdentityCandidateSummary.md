# V3E PATCH004 - Faculty Identity Candidate Summary

## Purpose

PATCH004 creates a review-facing summary layer for faculty identity disambiguation.

It aggregates evidence from:

```text
openalex.FacultyIdentityCandidate
openalex.FacultyIdentityCandidateWork
openalex.FacultyIdentityWorkEvidenceScore
```

into one row per:

```text
Faculty + Candidate OpenAlex Author ID
```

## New worker job

```text
FacultyIdentityCandidateSummaryGeneration
```

The job honors:

```json
"CandidateGeneration": {
  "PilotCohortName": "Pilot Expansion 15 Faculty"
}
```

## New table

Run first:

```sql
sql/230_V3E_PATCH004_FacultyIdentityCandidateSummary.sql
```

The table is:

```text
openalex.FacultyIdentityCandidateSummary
```

Important fields:

```text
FacultyId
FacultyDisplayName
CandidateAuthorOpenAlexId
CandidateAuthorOpenAlexShortId
CandidateDisplayName
CandidateOrcid
DiscoveryMethodCount
DiscoveryMethods
BestIdentityEvidenceScore
BestIdentityEvidenceStatus
BestCandidateType
DistinctExpandedWorks
StrongWorkEvidenceRows
ModerateWorkEvidenceRows
WeakWorkEvidenceRows
ConflictingWorkEvidenceRows
AverageWorkEvidenceScore
BestWorkEvidenceScore
IdentityReviewTriageStatus
ReviewPriority
SummaryText
EvidenceJson
```

## Triage statuses

PATCH004 produces review triage labels:

```text
LikelyIdentity
PossibleSplitProfile
NeedsManualReview
WeakCandidate
LikelyFalsePositive
```

These are not final decisions. They are review-priority labels.

## What this patch does not do

PATCH004 does not:

```text
auto-accept identity candidates
auto-reject identity candidates
merge OpenAlex author profiles
create publication candidates
change FacultyAuthorMatchDecision
change FacultyPublicationCandidate
```

## How to test locally

From repo root:

```powershell
git checkout v3e-patch004-identity-summary
git pull
```

Run the new SQL table script:

```sql
sql/230_V3E_PATCH004_FacultyIdentityCandidateSummary.sql
```

Build:

```powershell
dotnet build OpenAlexFacultyPublications.sln
```

Make sure PATCH001A, PATCH002, and PATCH003 have already populated:

```text
openalex.FacultyIdentityCandidate
openalex.FacultyIdentityCandidateWork
openalex.FacultyIdentityWorkEvidenceScore
```

Then run:

```powershell
dotnet run --project .\src\OpenAlex.Worker\OpenAlex.Worker.csproj
```

The worker is configured for:

```json
"JobName": "FacultyIdentityCandidateSummaryGeneration"
```

After it completes, run:

```sql
sql/231_V3E_PATCH004_IdentityCandidateSummary_Diagnostics_SQL2016.sql
```

## What success looks like

Diagnostics should show:

```text
SummaryRows
LikelyIdentityRows
PossibleSplitProfileRows
NeedsManualReviewRows
WeakCandidateRows
LikelyFalsePositiveRows
```

For Susan Pollart, the expected shape is:

```text
A5019353041 | Susan M. Pollart | likely identity
A5019834432 | S POLLART        | possible split profile or needs manual review
other candidates                  | weak candidate or likely false positive
```

## Next logical patch

The next patch should be:

```text
V3E PATCH005 - Identity Review UI
```

That patch can display the summary table and allow human review actions, but it still should not auto-generate publications until identities are accepted.
