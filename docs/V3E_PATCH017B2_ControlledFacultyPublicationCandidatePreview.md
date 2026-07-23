# V3E PATCH017B2 - Controlled Faculty-Publication Candidate Generation Preview

## Purpose

PATCH017B2 previews whether the five works inserted by one explicit completed PATCH017B1 backfill run can safely become faculty-publication candidates for one faculty member.

## Safety boundary

PATCH017B2 is read-only with respect to production matching tables.

It does not call `CandidateGenerationRepository.UpsertPublicationCandidateAsync` and does not insert or update:

```text
openalex.FacultyPublicationCandidate
openalex.FacultyPublicationDecision
```

It stores only preview and audit rows.

## Branch

```text
v3e-patch017b2-controlled-faculty-publication-candidate-preview
```

## Preview classifications

```text
ReadyToGenerate
AlreadyCandidateExists
NoAcceptedAuthorDecision
MultipleAcceptedAuthorDecisions
AcceptedAuthorNotOnWork
```

## Proposed score

PATCH017B2 reproduces the current base publication-candidate score without inserting a candidate:

```text
Accepted author appears on work: 80
Corresponding author:             +5
Known matching institution:      +10
Any authorship institution:       +5
Maximum:                         100
```

## Run instructions

Create the preview tables:

```sql
sql/374_V3E_PATCH017B2_FacultyPublicationCandidatePreview_SQL2016.sql
```

Find the latest completed PATCH017B1 run:

```sql
SELECT MAX(AcademicOnlineWorkBackfillRunId) AS SourceWorkBackfillRunId
FROM openalex.AcademicOnlineWorkBackfillRun
WHERE RunStatus = 'Completed';
```

Run the worker using that explicit ID:

```powershell
dotnet run --project src/OpenAlex.Worker/OpenAlex.Worker.csproj -- `
  --Worker:JobName=FacultyPublicationCandidatePreview `
  --Worker:RunOnceAndStop=true `
  --FacultyPublicationCandidatePreview:SourceWorkBackfillRunId=5 `
  --FacultyPublicationCandidatePreview:FacultyId=16 `
  --FacultyPublicationCandidatePreview:MaxWorks=5
```

Replace `5` if the latest completed PATCH017B1 run has a different ID.

Run diagnostics:

```sql
sql/375_V3E_PATCH017B2_FacultyPublicationCandidatePreview_Diagnostics_SQL2016.sql
```

## Expected successful outcome

```text
RunStatus: Completed
SelectedWorkCount: 5
ReadyToGenerateCount: 5
CandidateRowsBefore = CandidateRowsAfter
DecisionRowsBefore = DecisionRowsAfter
SafetyStatus: PreviewOnly_NoCandidateOrDecisionChange
```

A different classification is not necessarily an error. It indicates that identity, authorship, or duplicate-candidate evidence needs review before any candidate insert is allowed.
