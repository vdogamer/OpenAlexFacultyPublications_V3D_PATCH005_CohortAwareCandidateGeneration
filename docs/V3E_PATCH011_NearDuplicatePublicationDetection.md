# V3E PATCH011 - Near-Duplicate Publication Detection

## Purpose

PATCH011 flags publication review cards that may represent the same real-world publication even though they are different OpenAlex work records.

PATCH010 handles exact duplicate candidates by grouping on:

```text
FacultyId + WorkId
```

PATCH011 handles the next problem:

```text
Different WorkId values with very similar titles, same publication year, same faculty, and same matched author.
```

## Branch

```text
v3e-patch011-near-duplicate-publication-detection
```

## What changed

PATCH011 modifies:

```text
src/OpenAlex.Data/ReviewQueueRepository.cs
src/OpenAlex.Web/Pages/Review/Publications.cshtml
```

PATCH011 adds:

```text
sql/300_V3E_PATCH011_NearDuplicatePublicationDetection_Diagnostics_SQL2016.sql
docs/V3E_PATCH011_NearDuplicatePublicationDetection.md
```

## How near-duplicate detection works

The review queue now computes a simple normalized title key:

```text
lowercase title
trimmed title
common punctuation removed
first 90 normalized characters
```

Then it groups visible review cards by:

```text
FacultyId
PublicationYear
Matched author display name
Normalized title key
```

If more than one visible card appears in that group, PATCH011 appends warning evidence to the card.

## What reviewers see

If a card is part of a possible near-duplicate group, the UI shows:

```text
Near-duplicate group: 2 works
```

and a warning message:

```text
Possible near-duplicate publication group: similar title, publication year, faculty, and matched author appear across 2 OpenAlex work records
```

## What PATCH011 does not do

PATCH011 does not:

```text
delete rows
hide rows
merge rows
auto-accept rows
auto-reject rows
change matching score
```

It only warns the reviewer.

## Why this matters

Exact duplicate grouping is not enough because OpenAlex can contain separate work records with very similar or duplicate-looking titles.

The reviewer needs to know:

```text
These may be the same real-world publication.
Review carefully before accepting both.
```

## How to test

Checkout:

```powershell
git fetch
git checkout v3e-patch011-near-duplicate-publication-detection
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

Run diagnostics:

```sql
sql/300_V3E_PATCH011_NearDuplicatePublicationDetection_Diagnostics_SQL2016.sql
```

## Expected result

If the queue contains different OpenAlex works with similar titles for the same faculty/year/author, diagnostics should show them as possible near-duplicate groups.

The web page should show a near-duplicate warning badge on those cards.

If no near-duplicates exist, diagnostics may return no rows in the first two sections, and that is acceptable.

## Future improvement

A later patch can add reviewer actions such as:

```text
Mark as duplicate of another candidate
Suppress duplicate warning
Link related work records
```

But PATCH011 intentionally limits itself to warning only.
