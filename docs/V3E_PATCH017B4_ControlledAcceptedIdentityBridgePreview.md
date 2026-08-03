# V3E PATCH017B4 — Controlled Accepted-Identity Bridge Preview

## Purpose

PATCH017B4 previews the existing accepted-identity bridge for one faculty member and one explicit completed PATCH017B3 identity-resolution preview run.

It answers:

- Which PATCH017B3 author candidates now have a current `AcceptedIdentity` decision?
- Would the existing bridge insert or update an `AcceptedIdentityDecision` author candidate?
- Would it insert a current accepted faculty-author decision?
- Has an identity already been bridged?
- Is the matching local `openalex.Author` row present?

## Safety boundary

PATCH017B4 is preview-only. It does not invoke:

```text
AcceptedIdentityPublicationBridgeRepository.SyncAcceptedIdentitiesAsync
```

It does not write:

```text
openalex.FacultyAuthorMatchCandidate
openalex.FacultyAuthorMatchDecision
openalex.FacultyPublicationCandidate
openalex.FacultyPublicationDecision
```

It writes only its own preview audit tables:

```text
openalex.AcceptedIdentityBridgePreviewRun
openalex.AcceptedIdentityBridgePreviewItem
```

## Inputs

```text
AcceptedIdentityBridgePreview:SourceIdentityResolutionPreviewRunId
AcceptedIdentityBridgePreview:FacultyId
AcceptedIdentityBridgePreview:MaxIdentities
```

`SourceIdentityResolutionPreviewRunId` is required and must identify a completed PATCH017B3 run.

The job is limited to at most five identities.

## Proposed candidate actions

```text
InsertAcceptedIdentityCandidate
UpdateAcceptedIdentityCandidate
None
```

The existing bridge uses:

```text
CandidateSource = AcceptedIdentityDecision
CandidateStatus = AutoAcceptedCandidate
```

## Proposed decision actions

```text
InsertAcceptedAuthorDecision
SkipAlreadyAcceptedDecision
None
```

The existing bridge uses:

```text
DecisionStatus = Accepted
DecisionReason = AcceptedIdentityDecision
DecisionSource = IdentityReviewBridge
```

## Preview statuses

```text
ReadyToCreateCandidateAndDecision
ReadyToRefreshCandidateAndCreateDecision
ReadyToCreateBridgeCandidateOnly
ReadyToBridgeWithMissingLocalAuthorWarning
AlreadyBridged
BlockedMissingIdentitySummary
```

## Run sequence

1. Pull and build PATCH017B4.
2. Run `sql/378_V3E_PATCH017B4_AcceptedIdentityBridgePreview_SQL2016.sql`.
3. Find the completed PATCH017B3 run ID.
4. Run the worker with `JobName=AcceptedIdentityBridgePreview`.
5. Run `sql/379_V3E_PATCH017B4_AcceptedIdentityBridgePreview_Diagnostics_SQL2016.sql`.
6. Confirm `PreviewOnly_NoCandidateOrDecisionChange`.

## Example worker command

```powershell
dotnet run --project src/OpenAlex.Worker/OpenAlex.Worker.csproj -- `
  --Worker:JobName=AcceptedIdentityBridgePreview `
  --Worker:RunOnceAndStop=true `
  --AcceptedIdentityBridgePreview:SourceIdentityResolutionPreviewRunId=1 `
  --AcceptedIdentityBridgePreview:FacultyId=16 `
  --AcceptedIdentityBridgePreview:MaxIdentities=5
```

There is no backtick after the final line.

## Expected Charles Farber outcome

When the supported identities have been accepted in Identity Review, PATCH017B4 should normally show one row per current accepted identity.

A not-yet-bridged identity with a local author row should show:

```text
ProposedCandidateAction = InsertAcceptedIdentityCandidate
ProposedDecisionAction = InsertAcceptedAuthorDecision
PreviewStatus = ReadyToCreateCandidateAndDecision
ProposedNextAction = RunControlledAcceptedIdentityBridge
```

After a future controlled bridge execution, rerunning PATCH017B4 should show `AlreadyBridged`.
