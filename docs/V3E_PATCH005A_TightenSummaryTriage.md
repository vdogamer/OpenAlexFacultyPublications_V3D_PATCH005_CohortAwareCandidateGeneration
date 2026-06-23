# V3E PATCH005A - Tighten Identity Summary Triage

## Purpose

PATCH005A tightens the identity summary triage rules after testing the Martens demo case.

The problem found during testing was that candidates with the same last name and only one or two works could be promoted to `LikelyIdentity` too easily.

## Key rule change

A candidate can now become `LikelyIdentity` only when it is a `PrimaryIdentityCandidate` with high identity evidence and one of the following is true:

```text
1. The candidate is backed by a verified identity seed, such as ORCID lookup or claimed OpenAlex author ID.
2. The candidate has enough supporting work evidence volume.
```

## Verified identity seed signals

The worker treats these as verified seed signals:

```text
ClaimedOpenAlexAuthorId
OrcidLookup
StrongIdentifierCandidate
```

## Work volume threshold for non-seeded LikelyIdentity

A non-seeded candidate must now have:

```text
DistinctExpandedWorks >= 5
ScoredWorkRows >= 5
StrongWorkEvidenceRows >= 3
BestWorkEvidenceScore >= 75
AverageWorkEvidenceScore >= 65
```

This prevents tiny same-last-name profiles from becoming `LikelyIdentity` unless they are backed by a verified identifier.

## Possible split profile tightening

`PossibleProfileFragment` rows are also tightened. A non-seeded fragment now needs at least five distinct/scored works and meaningful work evidence before becoming `PossibleSplitProfile`.

## Expected Martens behavior

After rerunning summary generation:

```text
A5000288080 | Jeffrey R. Martens | LikelyIdentity
```

because it is backed by Jeff's ORCID/OpenAlex seed and has strong work evidence.

Small profiles such as:

```text
Jeffrey D. Martens
Jeffrey W. Martens
```

should no longer be promoted to `LikelyIdentity` based only on one or two works.

## How to test

Checkout the branch:

```powershell
git checkout v3e-patch005a-tighten-summary-triage
git pull
```

Build:

```powershell
dotnet build OpenAlexFacultyPublications.sln
```

Run only summary generation:

```powershell
dotnet run --project .\src\OpenAlex.Worker\OpenAlex.Worker.csproj
```

Then run:

```sql
sql/243_V3E_PATCH005_MartensPipelineDiagnostics_SQL2016.sql
```

Finally, open:

```text
/Review/Identities
```

Search for:

```text
Martens
```

## What this patch does not do

PATCH005A does not:

```text
change the Identity Review UI layout
auto-accept identity candidates
generate publication candidates
accept publications
merge OpenAlex author profiles
```
