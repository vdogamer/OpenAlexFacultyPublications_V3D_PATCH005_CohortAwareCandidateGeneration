# V3E PATCH006 - Publication Candidates from Accepted Identity Decisions

## Purpose

PATCH006 connects the new V3E identity-review workflow to the existing publication-review workflow.

After a reviewer records an identity decision as:

```text
AcceptedIdentity
```

PATCH006 bridges that accepted identity into the existing accepted-author-decision pipeline so the existing publication candidate generator can find works for that accepted author identity.

## Guardrail

Only current `AcceptedIdentity` decisions are bridged.

These identity decisions do not feed publication candidate generation:

```text
RejectedIdentity
NeedsMoreEvidence
non-current decisions
```

## What PATCH006 adds

```text
AcceptedIdentityPublicationBridgeRepository
AcceptedIdentityPublicationBridgeResult model
Publication candidate generation bridge call
Diagnostics SQL
Documentation
```

## How it works

PATCH006 does not directly accept publications.

Instead, it does this controlled bridge:

```text
FacultyIdentityCandidateDecision AcceptedIdentity
    -> FacultyAuthorMatchCandidate CandidateSource = AcceptedIdentityDecision
    -> FacultyAuthorMatchDecision DecisionSource = IdentityReviewBridge, DecisionStatus = Accepted
    -> existing FacultyPublicationCandidateGeneration logic
    -> FacultyPublicationCandidate rows
    -> normal Publication Review UI
```

This preserves the existing publication-review process.

## Why bridge through FacultyAuthorMatchDecision?

The existing publication candidate generator already knows how to generate publication candidates from current accepted author decisions.

Rather than creating a competing publication-generation path, PATCH006 converts accepted identity decisions into explicit accepted author decisions with a clear source:

```text
DecisionSource = IdentityReviewBridge
DecisionReason = AcceptedIdentityDecision
CandidateSource = AcceptedIdentityDecision
```

That keeps the audit trail visible.

## Files added

```text
src/OpenAlex.Data/AcceptedIdentityPublicationBridgeRepository.cs
src/OpenAlex.Data/Models/AcceptedIdentityPublicationBridgeModels.cs
sql/250_V3E_PATCH006_PublicationsFromAcceptedIdentities_Diagnostics_SQL2016.sql
docs/V3E_PATCH006_PublicationsFromAcceptedIdentities.md
```

## Files modified

```text
src/OpenAlex.Data/OpenAlexDataServiceCollectionExtensions.cs
src/OpenAlex.Worker/Jobs/FacultyPublicationCandidateGenerationJob.cs
src/OpenAlex.Worker/appsettings.json
```

## Required preconditions

Before running PATCH006, you should have:

```text
PATCH004 summary rows
PATCH005 decision table
At least one current AcceptedIdentity decision
Local Work/WorkAuthorship rows for the accepted OpenAlex author
```

For the Martens demo, first accept:

```text
A5000288080 | Jeffrey R. Martens
```

in:

```text
/Review/Identities
```

## How to test

Checkout branch:

```powershell
git checkout v3e-patch006-publications-from-accepted-identities
git pull
```

Build:

```powershell
dotnet build OpenAlexFacultyPublications.sln
```

Run publication candidate generation:

```powershell
dotnet run --project .\src\OpenAlex.Worker\OpenAlex.Worker.csproj
```

Then run diagnostics:

```sql
sql/250_V3E_PATCH006_PublicationsFromAcceptedIdentities_Diagnostics_SQL2016.sql
```

Finally, open:

```text
/Review/Publications
```

Search visually for the faculty whose identity was accepted.

## Expected Martens behavior

After accepting:

```text
A5000288080 | Jeffrey R. Martens
```

PATCH006 should create or update:

```text
FacultyAuthorMatchCandidate rows with CandidateSource = AcceptedIdentityDecision
FacultyAuthorMatchDecision rows with DecisionSource = IdentityReviewBridge
FacultyPublicationCandidate rows sourced through that bridged accepted author decision
```

## Important limitation

PATCH006 uses the existing publication candidate generator, which joins through local `openalex.Work` and `openalex.WorkAuthorship` tables.

If identity work expansion found works in `openalex.FacultyIdentityCandidateWork`, but those works are not present in the local `openalex.Work` / `openalex.WorkAuthorship` tables, PATCH006 may bridge the accepted identity but generate fewer publication candidates than the identity work-expansion count.

A future patch can backfill local `openalex.Work` rows from identity work expansion if needed.

## Next logical patch

```text
V3E PATCH007 - Backfill Work Rows from Accepted Identity Work Evidence
```

Only do this if PATCH006 shows accepted identities but too few publication candidates due to missing local Work rows.
