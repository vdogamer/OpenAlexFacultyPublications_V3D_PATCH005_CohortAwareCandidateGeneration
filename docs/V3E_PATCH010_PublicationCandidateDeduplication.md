# V3E PATCH010 - Publication Candidate Deduplication and Review Grouping

## Purpose

PATCH010 prevents reviewers from seeing multiple review cards for the same faculty/publication pair.

The rule is:

```text
One faculty member + one OpenAlex work = one review card.
```

This patch does not delete duplicate candidate rows. It only groups the review display and keeps the best candidate visible.

## Branch

```text
v3e-patch010-publication-candidate-dedup-review-grouping
```

## What changed

PATCH010 modifies:

```text
src/OpenAlex.Data/ReviewQueueRepository.cs
src/OpenAlex.Web/Pages/Review/Publications.cshtml
```

PATCH010 adds:

```text
sql/290_V3E_PATCH010_PublicationCandidateDeduplication_Diagnostics_SQL2016.sql
docs/V3E_PATCH010_PublicationCandidateDeduplication.md
```

## How grouping works

The publication review queue now groups pending candidates by:

```text
FacultyId + WorkId
```

If `WorkId` is missing, it falls back to:

```text
FacultyId + WorkOpenAlexId
```

If that is also missing, it falls back to:

```text
FacultyId + WorkTitle
```

Within each group, PATCH010 shows the best candidate using this ordering:

```text
highest MatchScore
newest CreatedAtUtc
highest FacultyPublicationCandidateId
```

## What reviewers see

If duplicates exist, the review card shows a badge like:

```text
Grouped: 2 candidates
```

It also appends evidence text explaining:

```text
Best-scoring candidate shown for this faculty/work pair; hidden duplicate candidate rows=1
```

## What PATCH010 does not do

PATCH010 does not:

```text
delete duplicate rows
merge candidate records
change accepted/rejected decisions
change candidate-generation logic
```

This is intentional. The patch is safe because it only improves the review queue display.

## How to test

Checkout:

```powershell
git fetch
git checkout v3e-patch010-publication-candidate-dedup-review-grouping
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
sql/290_V3E_PATCH010_PublicationCandidateDeduplication_Diagnostics_SQL2016.sql
```

## Expected result

If duplicate candidates exist, diagnostics should show duplicate groups.

The web page should show one review card per faculty/work pair and display a grouped-candidate badge.

## Why this matters

The reviewer should make one decision per publication match, not repeat the same decision multiple times because the evidence pipeline discovered the same work through multiple paths.
