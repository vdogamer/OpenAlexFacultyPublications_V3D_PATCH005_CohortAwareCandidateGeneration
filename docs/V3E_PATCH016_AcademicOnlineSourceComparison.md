# V3E PATCH016 - Academic Online Source Comparison

## Purpose

PATCH016 adds a read-only Academic Online comparison milestone.

The goal is to compare Academic Online scholar/publication data against our local OpenAlex candidate and accepted-publication data.

## Safety boundary

PATCH016 does not change OpenAlex ingestion, scoring, candidate generation, review decisions, accepted publications, or near-duplicate decisions.

It only stores Academic Online snapshots and provides diagnostics.

## Branch

```text
v3e-patch016-academic-online-source-comparison
```

## Setup

Run this SQL first:

```sql
sql/350_V3E_PATCH016_AcademicOnlineSnapshot_SQL2016.sql
```

Set the Academic Online API key outside GitHub:

```powershell
setx AcademicOnline__ApiKey "YOUR_API_KEY"
```

Run the worker job for one faculty:

```powershell
dotnet run --project src/OpenAlex.Worker/OpenAlex.Worker.csproj -- Worker:JobName=AcademicOnlineComparison Worker:RunOnceAndStop=true AcademicOnlineComparison:MaxFacultyToProcess=1
```

Run diagnostics:

```sql
sql/351_V3E_PATCH016_AcademicOnlineComparison_Diagnostics_SQL2016.sql
```

## What is captured

PATCH016 captures:

```text
Academic Online scholar search result
Academic Online scholar general profile
Academic Online unverified publication page
Academic Online verified publication page
raw JSON snapshots
```

## What the diagnostics compare

The diagnostics compare Academic Online publications to local records by:

```text
OpenAlex work ID
DOI
normalized title + publication year
```

## Interpretation

If Academic Online and local OpenAlex share the same OpenAlex work ID, both systems are seeing the same OpenAlex source work.

If Academic Online shows fewer publications, especially with verified=true, then Academic Online is likely applying a filtered or verified layer.

If Academic Online has publications not found locally, then either our OpenAlex ingest is incomplete or Academic Online is using additional sources.
