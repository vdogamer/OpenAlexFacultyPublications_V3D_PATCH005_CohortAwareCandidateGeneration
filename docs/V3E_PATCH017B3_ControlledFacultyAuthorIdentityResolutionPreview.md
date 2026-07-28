# V3E PATCH017B3 - Controlled Faculty-Author Identity Resolution Preview

## Purpose

PATCH017B3 ranks the OpenAlex authors appearing on the works inserted by one explicit PATCH017B1 backfill run.

The immediate pilot target is:

```text
FacultyId: 16
Faculty: Charles R. Farber
Source PATCH017B1 backfill run: 5
Maximum works: 5
```

## Safety boundary

PATCH017B3 is preview-only.

It does not insert or update:

```text
openalex.FacultyIdentityCandidate
openalex.FacultyIdentityCandidateDecision
openalex.FacultyAuthorMatchDecision
```

It records row counts before and after the preview to verify that those tables were unchanged.

## Evidence used

The preview compares each relevant OpenAlex author against:

```text
Faculty name
Faculty ORCID
Faculty claimed OpenAlex author ID
Academic Online name
Academic Online ORCID
Appearance across the selected works
Corresponding-author evidence
Authorship institution evidence
University of Virginia affiliation evidence
Existing identity-candidate summary
Existing identity decision
Existing accepted faculty-author decision
```

## Preview statuses

```text
ExistingAcceptedIdentity
ExactOrcidAndAuthorshipMatch
StrongIdentityCandidate
MultipleStrongCandidatesNeedReview
ExistingIdentityCandidateNeedsReview
ConsistentAuthorshipNeedsReview
CandidateNeedsMoreEvidence
```

## Proposed next actions

```text
IdentityAlreadyAccepted_RerunPATCH017B2
ReviewExistingIdentityCandidate
RunIdentityCandidatePipelineForFaculty
ManualIdentityInvestigation
GatherMoreIdentityEvidence
```

## Run instructions

Create the preview tables:

```sql
sql/376_V3E_PATCH017B3_FacultyAuthorIdentityResolutionPreview_SQL2016.sql
```

Run the preview:

```powershell
dotnet run --project src/OpenAlex.Worker/OpenAlex.Worker.csproj -- `
  --Worker:JobName=FacultyAuthorIdentityResolutionPreview `
  --Worker:RunOnceAndStop=true `
  --FacultyAuthorIdentityResolutionPreview:SourceWorkBackfillRunId=5 `
  --FacultyAuthorIdentityResolutionPreview:FacultyId=16 `
  --FacultyAuthorIdentityResolutionPreview:MaxWorks=5
```

Run diagnostics:

```sql
sql/377_V3E_PATCH017B3_FacultyAuthorIdentityResolutionPreview_Diagnostics_SQL2016.sql
```

## Expected best-case result

For Charles Farber, the ideal result is one top-ranked author with:

```text
ExactFacultyOrcidMatch = 1
ExactAcademicOnlineOrcidMatch = 1
AppearsOnAllSelectedWorks = 1
PreviewStatus = ExactOrcidAndAuthorshipMatch
SafetyStatus = PreviewOnly_NoIdentityOrAuthorDecisionChange
```

PATCH017B3 does not accept that identity automatically. The evidence must still flow through the existing Identity Review process.
