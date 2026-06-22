# V3E PATCH001A - Identity Candidate Triage Cleanup

## Purpose

PATCH001 proved the identity-candidate layer works, but it also showed that OpenAlex name search is noisy.

This patch improves the candidate triage layer without accepting, rejecting, merging, or generating publications.

## Changes

### 1. Rename misleading discovery method

Changed:

```text
ExactFullNameSearch
```

to:

```text
FullNameSearch
```

Reason: OpenAlex `search=` is not exact. It is a broad/fuzzy search and can return people whose names do not exactly match the faculty member.

### 2. Tighten name-based scoring

PATCH001A now uses candidate-name evidence more heavily.

High scores require stronger name evidence:

```text
Exact full-name match
First + last name match
First-initial + last-name match
Name contains faculty first and last tokens
Last-name-only match
```

Candidates with no meaningful name match and no identifier evidence are skipped.

### 3. Add candidate type tagging

Candidate type is written into `EvidenceSummary` and `EvidenceJson`:

```text
PrimaryIdentityCandidate
PossibleProfileFragment
WeakPossibleMatch
LikelyFalsePositive
```

This is not an acceptance decision. It is a triage label.

### 4. Clear stale generated rows before rerun

The job now deletes existing `openalex.FacultyIdentityCandidate` rows for the selected faculty before regenerating candidates.

Reason: PATCH001 created noisy rows. If we do not clear them, rerunning PATCH001A would mix old scoring with new scoring.

### 5. Add grouped diagnostics

New script:

```sql
sql/202_V3E_PATCH001A_IdentityCandidate_GroupedDiagnostics_SQL2016.sql
```

It shows one row per:

```text
Faculty + CandidateAuthorOpenAlexId
```

with:

```text
BestEvidenceScore
BestEvidenceStatus
BestCandidateType
DiscoveryMethodCount
DiscoveryMethods
```

## How to test

From repo root:

```powershell
git checkout v3e-patch001a-identity-triage
git pull
dotnet build OpenAlexFacultyPublications.sln
```

Run the worker:

```powershell
dotnet run --project .\src\OpenAlex.Worker\OpenAlex.Worker.csproj
```

Then run:

```sql
sql/201_V3E_PATCH001_IdentityCandidate_Diagnostics_SQL2016.sql
sql/202_V3E_PATCH001A_IdentityCandidate_GroupedDiagnostics_SQL2016.sql
```

## What should improve

For Susan Pollart, the strongest candidate should remain:

```text
A5019353041 | Susan M. Pollart | ORCID present
```

Possible fragments such as:

```text
S POLLART
Susan Pollart
```

should remain visible but should not be treated as accepted identities.

Obvious unrelated people returned by fuzzy OpenAlex search should be reduced or removed.

## What this patch still does not do

PATCH001A does not:

```text
auto-accept an identity
reject an identity
merge OpenAlex author profiles
expand works by candidate author ID
generate publication candidates from identity candidates
change human-review decisions
```

The next logical patch is still:

```text
V3E PATCH002 - Work Expansion by Candidate Author ID
```
