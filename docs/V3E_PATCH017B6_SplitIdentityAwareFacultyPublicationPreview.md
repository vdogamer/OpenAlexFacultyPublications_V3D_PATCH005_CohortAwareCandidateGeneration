# V3E PATCH017B6 — Split-Identity-Aware Faculty-Publication Candidate Preview

## Purpose

PATCH017B6 hardens the faculty-publication candidate preview for faculty members who legitimately have more than one accepted OpenAlex author identity.

PATCH017B5 proved this case for Charles R. Farber (FacultyId 16), who has three current accepted OpenAlex author identities. The older PATCH017B2 status gate treated the existence of more than one accepted author decision as an automatic conflict, even when one accepted identity clearly appeared on the specific work being evaluated.

PATCH017B6 changes that rule.

## Split-identity-aware rule

For each work:

1. Load the faculty member's current accepted author decisions.
2. Prefer an accepted identity that appears in the work's `openalex.WorkAuthorship` rows.
3. If the selected accepted identity appears on the work, the preview may be `ReadyToGenerate` even when the faculty member has multiple accepted identities overall.
4. If there is no accepted author decision, return `NoAcceptedAuthorDecision`.
5. If accepted identities exist but none appears on the work, return `AcceptedAuthorNotOnWork`.
6. Existing publication candidates remain `AlreadyCandidateExists`.

PATCH017B6 remains preview-only. It does not create or update `openalex.FacultyPublicationCandidate` rows and does not alter `openalex.FacultyPublicationDecision` rows.

## Audit version

The worker records:

```text
StartedBy: OpenAlex.V3E.PATCH017B6
ApplicationVersion: 3E-PATCH017B6-SplitIdentityAwarePublicationPreview
```

## Files changed

```text
src/OpenAlex.Worker/Jobs/FacultyPublicationCandidatePreviewJob.cs
src/OpenAlex.Worker/Jobs/FacultyPublicationCandidatePreviewOptions.cs
```

## Diagnostic added

```text
sql/382_V3E_PATCH017B6_SplitIdentityAwarePublicationPreview_Diagnostics_SQL2016.sql
```

No new preview tables are required. PATCH017B6 reuses the PATCH017B2 preview tables created by:

```text
sql/374_V3E_PATCH017B2_FacultyPublicationCandidatePreview_SQL2016.sql
```

## Controlled validation for FacultyId 16

Use the completed PATCH017B1 work backfill run:

```text
SourceWorkBackfillRunId = 5
FacultyId = 16
MaxWorks = 5
```

Run:

```powershell
dotnet run --project src/OpenAlex.Worker/OpenAlex.Worker.csproj -- `
  --Worker:JobName=FacultyPublicationCandidatePreview `
  --Worker:RunOnceAndStop=true `
  --FacultyPublicationCandidatePreview:SourceWorkBackfillRunId=5 `
  --FacultyPublicationCandidatePreview:FacultyId=16 `
  --FacultyPublicationCandidatePreview:MaxWorks=5
```

There is no backtick after the final line.

Then run:

```text
sql/382_V3E_PATCH017B6_SplitIdentityAwarePublicationPreview_Diagnostics_SQL2016.sql
```

## Expected result

Assuming each of the five controlled works contains one of Charles Farber's three accepted identities, expect approximately:

```text
RunStatus:                         Completed
SelectedWorkCount:                 5
ReadyToGenerateCount:              5
NoAcceptedAuthorDecisionCount:     0
MultipleAcceptedAuthorDecisionCount: 0
AcceptedAuthorNotOnWorkCount:      0
```

The per-work mapping should show the accepted OpenAlex identity selected for that work and a non-null `WorkAuthorshipId`.

The safety diagnostic must report:

```text
PreviewOnly_NoPublicationCandidateOrDecisionChange
```

## Important snapshot behavior

Older PATCH017B2 preview rows do not change after PATCH017B5 creates accepted author decisions. They are audit snapshots. You must run the preview worker again on PATCH017B6 to create a new preview run using the current accepted identities.

## Next step

When all five controlled works are `ReadyToGenerate`, proceed to a controlled faculty-publication candidate execution patch. That execution should create only the preview-approved `FacultyPublicationCandidate` rows and should still leave publication review decisions untouched.
