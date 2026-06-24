# V3E PATCH008A - Manual Review Rule Enforcement

## Purpose

PATCH008A makes the identity summary worker obey the faculty-specific review rules created in PATCH008.

The key idea is:

```text
For selected FacultyId records, identity summary triage becomes more cautious.
```

This protects known ambiguous faculty records without making assumptions from a person's name.

## Branch

```text
v3e-patch008a-manual-review-rules
```

## What changed

PATCH008A adds:

```text
src/OpenAlex.Data/Models/FacultyManualReviewRule.cs
src/OpenAlex.Data/FacultyManualReviewRuleRepository.cs
sql/272_V3E_PATCH008A_ManualReviewRuleEnforcement_Diagnostics_SQL2016.sql
docs/V3E_PATCH008A_ManualReviewRuleEnforcement.md
```

PATCH008A modifies:

```text
src/OpenAlex.Data/OpenAlexDataServiceCollectionExtensions.cs
src/OpenAlex.Worker/Jobs/FacultyIdentityCandidateSummaryGenerationJob.cs
```

## Enforcement behavior

The worker reads active rows from:

```text
openalex.FacultyIdentityReviewConstraint
```

For any matching `FacultyId`, the summary job applies the rule during identity triage.

### Rule 1

If:

```text
RequiresVerifiedIdentifierForLikelyIdentity = 1
```

then a candidate cannot become:

```text
LikelyIdentity
```

unless it has verified seed evidence.

Verified seed evidence currently means:

```text
ClaimedOpenAlexAuthorId
OrcidLookup
StrongIdentifierCandidate
```

### Rule 2

If:

```text
RequiresManualReviewForInitialOnlyName = 1
```

then candidates discovered through abbreviated search methods are held at:

```text
NeedsManualReview
```

unless verified seed evidence exists.

## What appears in summaries

PATCH008A appends rule evidence to:

```text
SummaryText
EvidenceJson
```

Look for fields like:

```text
HasManualReviewRule
IsAbbreviatedSearchCandidate
RequireVerifiedSeedForLikelyStatus
UseManualReviewForAbbreviatedSearch
RequireAuthorAffiliationSupport
RequireCollaborationOrTopicSupport
ManualReviewRuleReason
```

## How to test

Checkout:

```powershell
git fetch
git checkout v3e-patch008a-manual-review-rules
git pull
```

Build:

```powershell
dotnet build OpenAlexFacultyPublications.sln
```

Make sure PATCH008 setup has already been run:

```sql
sql/270_V3E_PATCH008_FacultyIdentityReviewConstraint_SQL2016.sql
```

Make sure the ambiguous faculty records have active rows in:

```text
openalex.FacultyIdentityReviewConstraint
```

Then run the summary job by setting:

```json
"JobName": "FacultyIdentityCandidateSummaryGeneration"
```

Run:

```powershell
dotnet run --project .\src\OpenAlex.Worker\OpenAlex.Worker.csproj
```

Then run diagnostics:

```sql
sql/272_V3E_PATCH008A_ManualReviewRuleEnforcement_Diagnostics_SQL2016.sql
```

## Expected result

Constrained faculty should show:

```text
HasManualReviewRule=True
```

in the summary evidence.

If a candidate would previously have become `LikelyIdentity` only because of strong work volume but lacks verified seed evidence, it should now remain:

```text
NeedsManualReview
```

## Notes

This patch does not auto-reject any candidate. It only prevents overly confident triage for faculty records that reviewers have explicitly marked for stricter review.
