# V3E PATCH012 - Near-Duplicate Publication Resolution Actions

## Purpose

PATCH012 lets reviewers resolve possible near-duplicate publication warnings created by PATCH011.

PATCH011 detects possible near-duplicates. PATCH012 lets a reviewer record what they decided about the warning.

## Branch

```text
v3e-patch012-near-duplicate-resolution-actions
```

## What PATCH012 adds

PATCH012 adds a new audit table:

```text
openalex.FacultyPublicationNearDuplicateResolution
```

It also adds three reviewer actions on `/Review/Publications`:

```text
Keep separate
Suppress warning
Mark duplicate of candidate
```

## What PATCH012 does not do

PATCH012 does not:

```text
delete candidate rows
merge OpenAlex work records
auto-accept a publication
auto-reject a publication
change the publication match score
```

It only records the reviewer's resolution of the warning.

## Setup SQL

Run:

```sql
sql/310_V3E_PATCH012_NearDuplicateResolution_SQL2016.sql
```

## Diagnostics SQL

Run:

```sql
sql/311_V3E_PATCH012_NearDuplicateResolution_Diagnostics_SQL2016.sql
```

## Resolution statuses

PATCH012 supports:

```text
DuplicateOfCandidate
KeepSeparate
SuppressWarning
```

### DuplicateOfCandidate

Use this when the current candidate appears to be a duplicate of another candidate.

The reviewer enters the related candidate ID.

### KeepSeparate

Use this when two records look similar but represent separate real-world publications.

### SuppressWarning

Use this when the warning is not useful and does not need repeated review.

## UI behavior

If a card is in a near-duplicate group and has no resolution, the card shows the PATCH011 warning and resolution action forms.

If a resolution exists, the card shows:

```text
Near-duplicate resolved: <status>
```

The warning text is suppressed because the reviewer has already addressed it.

## How to test

Checkout:

```powershell
git fetch
git checkout v3e-patch012-near-duplicate-resolution-actions
git pull
```

Run setup SQL:

```sql
sql/310_V3E_PATCH012_NearDuplicateResolution_SQL2016.sql
```

Build:

```powershell
dotnet build OpenAlexFacultyPublications.sln
```

Run the web app and open:

```text
/Review/Publications
```

For a near-duplicate group, try one of the resolution buttons.

Then run diagnostics:

```sql
sql/311_V3E_PATCH012_NearDuplicateResolution_Diagnostics_SQL2016.sql
```

## Expected result

The resolution should appear in diagnostics.

The publication review card should show the resolved status instead of the unresolved PATCH011 warning.

## Important reviewer workflow

PATCH012 separates two review decisions:

```text
Publication match decision: Accept / Reject
Near-duplicate warning decision: Keep separate / Suppress / Duplicate of candidate
```

A reviewer can resolve a warning without accepting or rejecting the publication candidate.
