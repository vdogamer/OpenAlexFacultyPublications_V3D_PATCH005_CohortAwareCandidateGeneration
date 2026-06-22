# V3E PATCH003 - Identity Work Evidence Scoring

## Purpose

PATCH003 scores the expanded works from PATCH002 as evidence for identity disambiguation.

The goal is to help answer:

```text
Does this candidate author ID contain works that look like they belong to this faculty member?
```

## What this patch does

Adds a new worker job:

```text
FacultyIdentityWorkEvidenceScoring
```

The job:

1. Honors `CandidateGeneration:PilotCohortName`.
2. Loads enabled active faculty from the pilot cohort.
3. Loads expanded works from `openalex.FacultyIdentityCandidateWork`.
4. Scores each work using conservative evidence signals.
5. Stores scores in `openalex.FacultyIdentityWorkEvidenceScore`.

## Scoring signals

PATCH003 scores each work using:

```text
NameEvidenceScore
InstitutionEvidenceScore
DoiEvidenceScore
PublicationYearEvidenceScore
OverallWorkEvidenceScore
```

Statuses:

```text
StrongWorkEvidence
ModerateWorkEvidence
WeakWorkEvidence
ConflictingWorkEvidence
NeedsManualReview
```

## What this patch does not do

PATCH003 does not:

```text
auto-accept identity candidates
auto-reject identity candidates
merge OpenAlex author profiles
create publication candidates
change FacultyAuthorMatchDecision
change FacultyPublicationCandidate
```

This patch only creates review evidence.

## New SQL table

Run first:

```sql
sql/220_V3E_PATCH003_FacultyIdentityWorkEvidenceScore.sql
```

## How to test locally

From repo root:

```powershell
git checkout v3e-patch003-work-evidence-scoring
git pull
```

Run the new SQL table script:

```sql
sql/220_V3E_PATCH003_FacultyIdentityWorkEvidenceScore.sql
```

Build:

```powershell
dotnet build OpenAlexFacultyPublications.sln
```

Make sure PATCH002 has already populated:

```text
openalex.FacultyIdentityCandidateWork
```

Then run:

```powershell
dotnet run --project .\src\OpenAlex.Worker\OpenAlex.Worker.csproj
```

The worker is configured for:

```json
"JobName": "FacultyIdentityWorkEvidenceScoring"
```

After it completes, run:

```sql
sql/221_V3E_PATCH003_WorkEvidenceScoring_Diagnostics_SQL2016.sql
```

## What success looks like

Diagnostics should show:

```text
CandidateAuthorsScored
ScoredWorkRows
DistinctScoredWorks
StrongWorkEvidenceRows
ModerateWorkEvidenceRows
WeakWorkEvidenceRows
ConflictingWorkEvidenceRows
AverageWorkEvidenceScore by candidate author
BestWorkEvidenceScore by candidate author
```

For Susan Pollart, this should help separate:

```text
A5019353041 | Susan M. Pollart | likely strong work evidence
A5019834432 | S POLLART        | possible fragment if work scores are strong/moderate
other names       | likely weak/conflicting if the matched author name does not align
```

## Next logical patch

After PATCH003, the next patch should create a review-facing identity summary:

```text
V3E PATCH004 - Faculty Identity Candidate Summary / Review Queue
```

That patch can aggregate candidate-level evidence, but it should still avoid automatic acceptance until the review UI is ready.
