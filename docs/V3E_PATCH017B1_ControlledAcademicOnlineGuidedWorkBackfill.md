# V3E PATCH017B1 - Controlled Academic Online-Guided OpenAlex Work Backfill

## Purpose

PATCH017B1 performs the first controlled insertion of canonical OpenAlex works identified by a successful PATCH017B preview.

It uses one explicit completed preview run and inserts at most five `ReadyToFetch` works through the existing `WorkIngestionRepository.UpsertWorkAsync` path.

## Branch

```text
v3e-patch017b1-controlled-academic-online-guided-work-backfill
```

## Safety boundary

PATCH017B1:

```text
Requires an explicit preview run ID
Requires ConfirmInsert=true
Limits execution to five works
Rechecks local Work immediately before insertion
Validates the preview payload OpenAlex ID
Uses canonical OpenAlex JSON captured by PATCH017B
Uses the existing work/authorship/institution ingestion path
Does not invoke publication candidate generation
Does not change publication decisions
Records candidate and decision counts before and after
```

## Setup SQL

Run:

```sql
sql/372_V3E_PATCH017B1_ControlledAcademicOnlineGuidedWorkBackfill_SQL2016.sql
```

This creates:

```text
openalex.AcademicOnlineWorkBackfillRun
openalex.AcademicOnlineWorkBackfillItem
```

## Run command

Use the explicit successful preview run ID. For the first test, the preview run shown by the PATCH017B diagnostics was `1`.

```powershell
dotnet run --project src/OpenAlex.Worker/OpenAlex.Worker.csproj -- `
  --Worker:JobName=AcademicOnlineWorkBackfill `
  --Worker:RunOnceAndStop=true `
  --AcademicOnlineWorkBackfill:PreviewRunId=1 `
  --AcademicOnlineWorkBackfill:ConfirmInsert=true `
  --AcademicOnlineWorkBackfill:MaxWorks=5
```

Do not place a PowerShell backtick after the final command line.

## Execution statuses

```text
Inserted
SkippedAlreadyExists
InvalidPreviewItem
InvalidPreviewPayload
PreviewIdMismatch
ConcurrentExistingWorkUpdated
InsertFailed
```

## Diagnostics

Run:

```sql
sql/373_V3E_PATCH017B1_ControlledAcademicOnlineGuidedWorkBackfill_Diagnostics_SQL2016.sql
```

Expected first-run result:

```text
SelectedWorkCount = 5
InsertedWorkCount = 5
AlreadyExistsCount = 0
InvalidPreviewItemCount = 0
FailedItemCount = 0
CandidateRowsBefore = CandidateRowsAfter
DecisionRowsBefore = DecisionRowsAfter
RunStatus = Completed
```

The final safety status should be:

```text
WorkBackfillOnly_NoCandidateOrDecisionChange
```

## What happens afterward

PATCH017B1 stops after verified work ingestion. Candidate generation remains a separate deliberate step so UVA can inspect the five inserted works before any new faculty-publication candidates are created.
