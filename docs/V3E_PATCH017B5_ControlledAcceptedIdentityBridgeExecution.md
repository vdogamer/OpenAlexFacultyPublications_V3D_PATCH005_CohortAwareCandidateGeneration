# V3E PATCH017B5 — Controlled Accepted-Identity Bridge Execution

## Purpose

Execute the bridge that converts the explicitly selected, currently accepted identity decisions from one completed PATCH017B4 preview run into:

- `openalex.FacultyAuthorMatchCandidate` rows with `CandidateSource = AcceptedIdentityDecision`;
- current accepted `openalex.FacultyAuthorMatchDecision` rows with `DecisionSource = IdentityReviewBridge`.

PATCH017B5 does not generate faculty-publication candidates and does not alter faculty-publication decisions.

## Required safety gates

The worker requires all of the following:

- an explicit completed PATCH017B4 preview run ID;
- an explicit FacultyId;
- `ConfirmBridge=true`;
- a maximum of five identities;
- each selected preview item must have a bridge-ready or already-bridged status;
- the exact identity decision selected during PATCH017B4 must still be current and `AcceptedIdentity`;
- the exact identity summary selected during PATCH017B4 must still exist.

If identity review changes after the preview, the transaction stops and rolls back. Run PATCH017B4 again before retrying.

## Audit lineage

PATCH017B5 creates:

- `openalex.AcceptedIdentityBridgeExecutionRun`;
- `openalex.AcceptedIdentityBridgeExecutionItem`;
- a dedicated valid `openalex.IngestionRun` row using `RunMode = TestLoad`.

The dedicated ingestion run supplies the required `IngestionRunId` lineage for generated author-match candidates. Its `InstitutionFilter` identifies the selected PATCH017B4 preview run.

## SQL scripts

Run in order:

```text
sql/380_V3E_PATCH017B5_AcceptedIdentityBridgeExecution_SQL2016.sql
sql/381_V3E_PATCH017B5_AcceptedIdentityBridgeExecution_Diagnostics_SQL2016.sql
```

## Find the completed PATCH017B4 preview run

```sql
SELECT
    AcceptedIdentityBridgePreviewRunId,
    FacultyId,
    SelectedAcceptedIdentityCount,
    ReadyCandidateInsertCount,
    ReadyCandidateUpdateCount,
    ReadyDecisionInsertCount,
    AlreadyAcceptedDecisionCount,
    MissingIdentitySummaryCount,
    MissingLocalAuthorCount,
    RunStatus,
    CompletedAtUtc
FROM openalex.AcceptedIdentityBridgePreviewRun
WHERE FacultyId = 16
  AND RunStatus = 'Completed'
ORDER BY AcceptedIdentityBridgePreviewRunId DESC;
```

Use the intended preview run ID in the worker command. Do not assume that it is `1`.

## Execute the controlled bridge

```powershell
dotnet run --project src/OpenAlex.Worker/OpenAlex.Worker.csproj -- `
  --Worker:JobName=AcceptedIdentityBridgeExecution `
  --Worker:RunOnceAndStop=true `
  --AcceptedIdentityBridgeExecution:PreviewRunId=<PATCH017B4_PREVIEW_RUN_ID> `
  --AcceptedIdentityBridgeExecution:FacultyId=16 `
  --AcceptedIdentityBridgeExecution:ConfirmBridge=true `
  --AcceptedIdentityBridgeExecution:MaxIdentities=3
```

There is no backtick after the final line.

## Expected first execution

When three supported identities are selected and none has been bridged previously, expect approximately:

```text
RunStatus: Completed
SelectedIdentityCount: 3
CandidateRowsInserted: 3
CandidateRowsUpdated: 0
DecisionRowsInserted: 3
DecisionsAlreadyAccepted: 0
```

A rerun against a fresh preview may update the existing bridge candidates and skip already accepted author decisions rather than duplicate them.

## Safety verification

The diagnostic should report:

```text
ControlledBridge_NoPublicationCandidateOrDecisionChange
```

Author candidate and author decision counts are expected to change according to the execution actions. Faculty-publication candidate and publication-decision counts must remain unchanged.

## Next step

After successful diagnostics, rerun PATCH017B2 using the completed PATCH017B1 work-backfill run. The five works should move from `NoAcceptedAuthorDecision` toward `ReadyToGenerate`, depending on which identities were bridged.
