# V3E PATCH017B7 — Controlled Faculty-Publication Candidate Execution

## Purpose

PATCH017B7 converts the exact `ReadyToGenerate` rows from one explicit completed PATCH017B6 split-identity-aware preview into real `openalex.FacultyPublicationCandidate` rows.

It creates candidates only. It does **not** accept or reject publications and does not write `openalex.FacultyPublicationDecision`.

## Safety gates

The worker requires:

- an explicit PATCH017B6 `FacultyPublicationCandidatePreviewRunId`;
- an explicit FacultyId;
- `ConfirmExecution=true`;
- at most five selected candidates;
- the source preview run must still be `Completed` and carry the PATCH017B6 application version;
- every selected preview row must still be `ReadyToGenerate`;
- each frozen accepted author decision must still be current and `Accepted`;
- the frozen OpenAlex work must still exist with the same ID;
- the frozen `WorkAuthorshipId` must still link that work to the selected accepted identity.

All candidate writes and per-item execution audit rows are performed inside one serializable transaction. If revalidation or the internal count safety check fails, the transaction rolls back.

## Candidate write contract

For a missing candidate, PATCH017B7 writes:

```text
CandidateSource = AcceptedAuthorMatch
CandidateStatus = PendingReview
MatchScore      = frozen PATCH017B6 ProposedMatchScore
```

It also preserves the frozen accepted-author decision, selected OpenAlex author identity, work metadata, evidence summary/JSON, and a dedicated `IngestionRunId` audit lineage.

If the same FacultyId + WorkId + `AcceptedAuthorMatch` candidate already exists, PATCH017B7 records `AlreadyExistsNoChange` and does not modify it.

## Files

```text
src/OpenAlex.Data/Models/FacultyPublicationCandidateExecutionModels.cs
src/OpenAlex.Data/FacultyPublicationCandidateExecutionRepository.cs
src/OpenAlex.Worker/Jobs/FacultyPublicationCandidateExecutionOptions.cs
src/OpenAlex.Worker/Jobs/FacultyPublicationCandidateExecutionJob.cs
src/OpenAlex.Worker/Program.cs
src/OpenAlex.Worker/OpenAlexWorkerService.cs
sql/383_V3E_PATCH017B7_FacultyPublicationCandidateExecution_SQL2016.sql
sql/384_V3E_PATCH017B7_FacultyPublicationCandidateExecution_Diagnostics_SQL2016.sql
```

## Current controlled pilot

The validated source is:

```text
PATCH017B6 PreviewRunId = 3
FacultyId               = 16
ReadyToGenerate         = 5
```

## Build

```powershell
git fetch
git checkout v3e-patch017b7-controlled-faculty-publication-candidate-execution
git pull

dotnet clean OpenAlexFacultyPublications.sln
dotnet build OpenAlexFacultyPublications.sln
```

## Create the execution audit schema

Run in SSMS against the test database:

```text
sql/383_V3E_PATCH017B7_FacultyPublicationCandidateExecution_SQL2016.sql
```

Both returned object IDs should be non-NULL.

## Execute the five controlled candidates

```powershell
dotnet run --project src/OpenAlex.Worker/OpenAlex.Worker.csproj -- `
  --Worker:JobName=FacultyPublicationCandidateExecution `
  --Worker:RunOnceAndStop=true `
  --FacultyPublicationCandidateExecution:PreviewRunId=3 `
  --FacultyPublicationCandidateExecution:FacultyId=16 `
  --FacultyPublicationCandidateExecution:ConfirmExecution=true `
  --FacultyPublicationCandidateExecution:MaxCandidates=5
```

There is no backtick after the final line.

## Diagnostics

Run:

```text
sql/384_V3E_PATCH017B7_FacultyPublicationCandidateExecution_Diagnostics_SQL2016.sql
```

For the first clean execution against the current test database, the expected result is approximately:

```text
RunStatus:                    Completed
SelectedCandidateCount:       5
CandidateRowsInserted:        5
CandidatesAlreadyExist:       0
ValidationFailureCount:       0
PublicationCandidateRowsBefore: 11
PublicationCandidateRowsAfter:  16
PublicationDecisionRowsBefore:   6
PublicationDecisionRowsAfter:    6
```

The critical safety classification is:

```text
ControlledExecution_CandidateInsertOnly_NoPublicationDecisionChange
```

The final next action should be:

```text
OpenPublicationReviewWorkflow
```

## After PATCH017B7

Start the web app and open:

```text
http://localhost:5000/Review/Publications
```

The five Charles R. Farber publication candidates should be available for normal human review. Publication acceptance remains a separate explicit action.
