# V3E PATCH009 - Publication Review Evidence Columns

## Purpose

PATCH009 improves the `/Review/Publications` page by surfacing publication-matching evidence in separate review columns.

Before this patch, reviewers had to read a long `EvidenceSummary` string to understand why a publication candidate was strong or weak.

PATCH009 makes the strongest evidence easier to scan:

```text
Institution Evidence
Coauthor Evidence
Evidence Boost
```

## Branch

```text
v3e-patch009-publication-review-evidence-columns
```

## What changed

PATCH009 modifies:

```text
src/OpenAlex.Web/Pages/Review/Publications.cshtml
```

PATCH009 adds:

```text
sql/280_V3E_PATCH009_PublicationReviewEvidenceColumns_Diagnostics_SQL2016.sql
docs/V3E_PATCH009_PublicationReviewEvidenceColumns.md
```

## New UI columns

The Publication Review Queue now includes:

```text
Institution Evidence
Coauthor Evidence
```

The score column also displays the PATCH007 evidence boost when available.

## Evidence source

PATCH009 reads the existing candidate `EvidenceSummary` field.

It extracts values created by PATCH007, including:

```text
AffiliatedInstitutionMatchCount
AffiliatedInstitutionEvidenceWeight
AffiliatedInstitutionNames
KnownCoauthorOverlapCount
KnownCoauthorHistoricalPublicationCount
KnownCoauthorNames
EvidenceBoost
```

This means PATCH009 does not require a schema change.

## What reviewers will see

For institution evidence, reviewers can see:

```text
number of affiliated institution matches
best institution evidence weight
matched institution names
```

For coauthor evidence, reviewers can see:

```text
known coauthor overlap count
prior accepted publication count
known coauthor names
```

For scoring, reviewers can see:

```text
candidate MatchScore
evidence Boost
```

## How to test

Checkout:

```powershell
git fetch
git checkout v3e-patch009-publication-review-evidence-columns
git pull
```

Build:

```powershell
dotnet build OpenAlexFacultyPublications.sln
```

Run the web app and open:

```text
/Review/Publications
```

Then run diagnostics:

```sql
sql/280_V3E_PATCH009_PublicationReviewEvidenceColumns_Diagnostics_SQL2016.sql
```

## Expected result

The page should still show pending publication candidates.

For candidates enhanced by PATCH007, the new columns should show institution and coauthor evidence separately.

If a candidate has not been enhanced by PATCH007, the page will show:

```text
No PATCH009 institution evidence
No PATCH009 coauthor evidence
```

That is expected until publication candidate generation has been run after PATCH007.

## Notes

This patch is UI-only plus diagnostics.

It does not change matching logic, review decisions, or scoring behavior.
