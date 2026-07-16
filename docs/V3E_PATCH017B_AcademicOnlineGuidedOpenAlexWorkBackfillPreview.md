# V3E PATCH017B - Academic Online-Guided OpenAlex Work Backfill Preview

## Purpose

PATCH017B previews a controlled OpenAlex work backfill using exact OpenAlex work IDs observed in Academic Online.

The patch is read-only with respect to the canonical OpenAlex ingestion tables. It does not insert or update `openalex.Work`, create publication candidates, or alter review decisions.

## Branch

```text
v3e-patch017b-academic-online-guided-openalex-backfill-preview
```

## Safety boundary

PATCH017B writes only to these preview/audit tables:

```text
openalex.AcademicOnlineWorkBackfillPreviewRun
openalex.AcademicOnlineWorkBackfillPreviewItem
```

It does not call `WorkIngestionRepository.UpsertWorkAsync`.

## Setup

Run:

```sql
sql/370_V3E_PATCH017B_AcademicOnlineGuidedWorkBackfillPreview_SQL2016.sql
```

Build the solution:

```powershell
dotnet clean OpenAlexFacultyPublications.sln
dotnet build OpenAlexFacultyPublications.sln
```

## Run the five-work preview

The default configuration uses:

```text
FacultyId = 16
MaxWorks = 5
AcademicOnlineComparisonRunId = latest completed run
```

Run:

```powershell
dotnet run --project src/OpenAlex.Worker/OpenAlex.Worker.csproj -- `
  --Worker:JobName=AcademicOnlineWorkBackfillPreview `
  --Worker:RunOnceAndStop=true `
  --AcademicOnlineWorkBackfillPreview:FacultyId=16 `
  --AcademicOnlineWorkBackfillPreview:MaxWorks=5
```

## Preview statuses

```text
ReadyToFetch
AlreadyExistsLocally
MissingOpenAlexId
NotFoundByOpenAlex
FetchFailed
```

`ReadyToFetch` means the exact work ID returned a successful OpenAlex entity response and the returned work ID matched the requested ID. It does not mean the work was inserted.

`SnapshotDuplicateCount` records how many Academic Online snapshot rows were consolidated into the selected preview item.

## Diagnostics

Run:

```sql
sql/371_V3E_PATCH017B_AcademicOnlineGuidedWorkBackfillPreview_Diagnostics_SQL2016.sql
```

Expected sections:

```text
1) Selected preview run
2) Preview status counts
3) Detailed preview items
4) Cross-source identity checks
5) Safety verification: preview did not write local Work rows
```

## Go/no-go rule for a later controlled insert patch

Proceed only when:

```text
ReadyToFetch = selected preview count
Returned OpenAlex IDs match requested IDs
Titles, years, and DOI values are reasonable
FetchFailed = 0
NotFoundByOpenAlex = 0
Safety verification confirms no local Work insert
```
