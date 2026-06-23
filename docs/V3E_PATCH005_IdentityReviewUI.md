# V3E PATCH005 - Identity Review UI

## Purpose

PATCH005 adds a web review interface for the V3E identity-disambiguation workflow.

It lets reviewers inspect one row per:

```text
Faculty + Candidate OpenAlex Author ID
```

and record a human identity-review decision.

## New URL

```text
/Review/Identities
```

The page is protected by the existing `ReviewerPolicy` because it lives under `/Review`.

## New SQL table

Run first:

```sql
sql/240_V3E_PATCH005_FacultyIdentityCandidateDecision.sql
```

The table is:

```text
openalex.FacultyIdentityCandidateDecision
```

Current decision statuses:

```text
AcceptedIdentity
RejectedIdentity
NeedsMoreEvidence
```

## What this patch does

PATCH005 adds:

```text
Identity review list page
Identity review detail page
Current decision storage
Decision history support through IsCurrent
Work evidence display for each candidate author
Navigation link in the main layout
Diagnostics SQL
```

## What this patch does not do

PATCH005 does not:

```text
auto-generate publication candidates from accepted identities
auto-accept publications
modify existing FacultyAuthorMatchDecision rows
modify existing FacultyPublicationCandidate rows
merge OpenAlex author profiles
```

This patch records identity decisions only.

## Files added

```text
sql/240_V3E_PATCH005_FacultyIdentityCandidateDecision.sql
sql/241_V3E_PATCH005_IdentityReviewUI_Diagnostics_SQL2016.sql
src/OpenAlex.Data/IdentityReviewRepository.cs
src/OpenAlex.Data/Models/IdentityReviewModels.cs
src/OpenAlex.Web/Pages/Review/Identities.cshtml
src/OpenAlex.Web/Pages/Review/Identities.cshtml.cs
src/OpenAlex.Web/Pages/Review/IdentityDetails.cshtml
src/OpenAlex.Web/Pages/Review/IdentityDetails.cshtml.cs
```

## Files modified

```text
src/OpenAlex.Data/OpenAlexDataServiceCollectionExtensions.cs
src/OpenAlex.Web/Pages/Shared/_Layout.cshtml
src/OpenAlex.Web/wwwroot/css/site.css
```

## How to test locally

From repo root:

```powershell
git checkout v3e-patch005-identity-review-ui
git pull
```

Run the new SQL table script:

```sql
sql/240_V3E_PATCH005_FacultyIdentityCandidateDecision.sql
```

Build:

```powershell
dotnet build OpenAlexFacultyPublications.sln
```

Run the web app:

```powershell
dotnet run --project .\src\OpenAlex.Web\OpenAlex.Web.csproj
```

Open:

```text
/Review/Identities
```

## Recommended demo flow

1. Run PATCH004 summary generation.
2. Open `/Review/Identities`.
3. Filter by `LikelyIdentity`, `PossibleSplitProfile`, or search for `Martens`.
4. Open a candidate detail page.
5. Review the work evidence and scores.
6. Record one of:

```text
Accept Identity
Needs More Evidence
Reject Identity
```

7. Run diagnostics:

```sql
sql/241_V3E_PATCH005_IdentityReviewUI_Diagnostics_SQL2016.sql
```

## Next logical patch

The next patch should be:

```text
V3E PATCH006 - Generate Publication Candidates from Accepted Identity Decisions
```

That patch should only use identities with a current `AcceptedIdentity` decision.
