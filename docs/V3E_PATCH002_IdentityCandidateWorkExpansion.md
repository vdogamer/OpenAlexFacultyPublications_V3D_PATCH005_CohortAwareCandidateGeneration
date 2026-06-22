# V3E PATCH002 - Identity Candidate Work Expansion

## Purpose

PATCH002 expands works by candidate OpenAlex author ID and stores the results as disambiguation evidence.

This patch answers questions like:

```text
Does the S POLLART OpenAlex author profile contain works that plausibly belong to Susan M. Pollart?
Does a candidate author profile contain early-career works outside UVA?
Which candidate author IDs have no useful work evidence?
```

## What this patch does

Adds a new worker job:

```text
FacultyIdentityCandidateWorkExpansion
```

The job:

1. Honors `CandidateGeneration:PilotCohortName`.
2. Loads selected faculty from `openalex.PilotFacultyCohort`.
3. Loads grouped identity candidates from `openalex.FacultyIdentityCandidate`.
4. Queries OpenAlex works by candidate author ID:

```text
filter=authorships.author.id:<AuthorId>
```

5. Stores expanded works in:

```text
openalex.FacultyIdentityCandidateWork
```

## What this patch does not do

PATCH002 does not:

```text
auto-accept author identities
reject author identities
merge OpenAlex author profiles
create publication candidates
change human review decisions
change FacultyAuthorMatchDecision
change FacultyPublicationCandidate
```

This is still evidence gathering.

## New SQL table

Run first:

```sql
sql/210_V3E_PATCH002_FacultyIdentityCandidateWork.sql
```

Important fields:

```text
FacultyId
CandidateAuthorOpenAlexId
CandidateAuthorOpenAlexShortId
WorkOpenAlexId
WorkOpenAlexShortId
WorkDisplayName
WorkDoi
PublicationYear
PublicationDate
WorkType
MatchedAuthorDisplayName
MatchedAuthorPosition
MatchedAuthorInstitutionsJson
AuthorshipsJson
PrimaryLocationJson
LocationsJson
WorkRawJson
EvidenceSummary
IngestionRunId
```

## New config options

```json
"CandidateGeneration": {
  "MaxIdentityWorksPerAuthor": 100,
  "MaxIdentityWorkPagesPerAuthor": 2,
  "MinimumIdentityCandidateEvidenceScoreForWorkExpansion": 55,
  "IncludeWeakIdentityCandidatesInWorkExpansion": true
}
```

The default settings intentionally include weak possible matches so we can inspect edge cases during the pilot. Later, after review, this can be tightened.

## How to test locally

From repo root:

```powershell
git checkout v3e-patch002-identity-work-expansion
git pull
```

Run the new table script:

```sql
sql/210_V3E_PATCH002_FacultyIdentityCandidateWork.sql
```

Build:

```powershell
dotnet build OpenAlexFacultyPublications.sln
```

Make sure PATCH001A has already populated:

```text
openalex.FacultyIdentityCandidate
```

Then run:

```powershell
dotnet run --project .\src\OpenAlex.Worker\OpenAlex.Worker.csproj
```

The worker is configured for:

```json
"JobName": "FacultyIdentityCandidateWorkExpansion"
```

After it completes, run:

```sql
sql/211_V3E_PATCH002_IdentityCandidateWork_Diagnostics_SQL2016.sql
```

## What success looks like

Diagnostics should show:

```text
Faculty rows from Pilot Expansion 15 Faculty
CandidateAuthorsWithWorks
ExpandedWorkRows
DistinctExpandedWorks
EarliestPublicationYear
LatestPublicationYear
latest FacultyIdentityCandidateWorkExpansion run summary
```

For Susan Pollart, look at the candidate author ID groups. The key question is not just how many works appear, but whether the work titles, publication years, and matched author names look like the same real person.

## Next logical patch

After PATCH002, the next step is:

```text
V3E PATCH003 - Identity Work Evidence Scoring
```

That patch should evaluate expanded works against faculty evidence such as name, institution, coauthors, topic, publication timeline, and DOI/PubMed signals.
