# V3E PATCH007 - Affiliated Institution and Known Coauthor Evidence

## Purpose

PATCH007 adds two human-review evidence signals to publication candidate scoring:

```text
Affiliated institution evidence
Known coauthor / lab-member overlap evidence
```

This patch was motivated by the observation that a strong publication match is often more than a name match. A new publication becomes much stronger when it includes:

```text
the faculty author identity
a UVA-affiliated institution
coauthors/lab members who have appeared on the faculty member's accepted publications
```

## What PATCH007 adds

```text
openalex.AffiliatedInstitutionAlias
PublicationEvidenceEnhancementRepository
PublicationEvidenceEnhancementResult model
PATCH007 diagnostics SQL
PATCH007 documentation
```

## What PATCH007 does not do

PATCH007 does not:

```text
auto-accept publications
auto-reject publications
change identity decisions
merge OpenAlex author profiles
```

It only strengthens evidence on publication candidates that are already being generated for review.

## New SQL setup script

Run:

```sql
sql/260_V3E_PATCH007_AffiliatedInstitutionAlias_SQL2016.sql
```

This creates and seeds:

```text
openalex.AffiliatedInstitutionAlias
```

Seeded affiliated institution names include:

```text
University of Virginia
University of Virginia Health System
University of Virginia Medical Center
University of Virginia Hospital
University of Virginia Children's Hospital
Inova Fairfax Hospital
Western State Hospital
```

## Evidence weights

The seeded aliases include evidence weights:

```text
University of Virginia                              100
University of Virginia Health System                95
University of Virginia Medical Center               95
University of Virginia Hospital                     95
University of Virginia Children's Hospital          95
Inova Fairfax Hospital                              80
Western State Hospital                              80
```

These are table-driven, so additional affiliated institutions can be added later without changing C#.

## How affiliated institution evidence works

For each publication candidate from the current worker run, PATCH007 checks the candidate work's authorship institutions.

If any institution on the work matches an active row in:

```text
openalex.AffiliatedInstitutionAlias
```

then the candidate receives extra evidence in:

```text
MatchScore
EvidenceSummary
EvidenceJson
```

## How known coauthor evidence works

PATCH007 uses accepted publications as the source of known coauthors.

For a new candidate publication, it checks whether the publication includes coauthors who have appeared on that faculty member's previously accepted publications.

The signal becomes stronger when:

```text
more known coauthors overlap
the overlapping coauthors have appeared on several accepted publications
```

## Scoring approach

PATCH007 adds a bounded evidence boost.

Affiliated institution boost:

```text
Best affiliated institution weight >= 90   +15
Best affiliated institution weight >= 70   +10
Any affiliated institution match            +5
```

Known coauthor overlap boost:

```text
3+ known coauthors overlap                 +20
2 known coauthors overlap                  +15
1 known coauthor overlaps                  +10
```

Known coauthor history boost:

```text
10+ historical accepted publications        +10
5+ historical accepted publications          +5
2+ historical accepted publications          +2
```

Final candidate `MatchScore` is capped at 100.

## Where the evidence appears

PATCH007 appends this text to `openalex.FacultyPublicationCandidate.EvidenceSummary`:

```text
PATCH007Evidence: AffiliatedInstitutionMatchCount=...;
AffiliatedInstitutionEvidenceWeight=...;
AffiliatedInstitutionNames=...;
KnownCoauthorOverlapCount=...;
KnownCoauthorHistoricalPublicationCount=...;
KnownCoauthorNames=...;
EvidenceBoost=...
```

This means the existing Publication Review UI should show the enhanced evidence without requiring a UI patch.

## Files added

```text
sql/260_V3E_PATCH007_AffiliatedInstitutionAlias_SQL2016.sql
sql/261_V3E_PATCH007_AffiliatedInstitutionCoauthorEvidence_Diagnostics_SQL2016.sql
src/OpenAlex.Data/PublicationEvidenceEnhancementRepository.cs
src/OpenAlex.Data/Models/PublicationEvidenceEnhancementModels.cs
docs/V3E_PATCH007_AffiliatedInstitutionCoauthorEvidence.md
```

## Files modified

```text
src/OpenAlex.Data/OpenAlexDataServiceCollectionExtensions.cs
src/OpenAlex.Worker/Jobs/FacultyPublicationCandidateGenerationJob.cs
```

## How to test

Checkout branch:

```powershell
git checkout v3e-patch007-affiliated-institution-coauthor-evidence
git pull
```

Run setup SQL:

```sql
sql/260_V3E_PATCH007_AffiliatedInstitutionAlias_SQL2016.sql
```

Build:

```powershell
dotnet build OpenAlexFacultyPublications.sln
```

Make sure the worker is running:

```json
"JobName": "FacultyPublicationCandidateGeneration"
```

Recommended application version:

```json
"ApplicationVersion": "3E-PATCH007-AffiliatedInstitutionCoauthorEvidence"
```

Then run:

```powershell
dotnet run --project .\src\OpenAlex.Worker\OpenAlex.Worker.csproj
```

Run diagnostics:

```sql
sql/261_V3E_PATCH007_AffiliatedInstitutionCoauthorEvidence_Diagnostics_SQL2016.sql
```

## Expected result

Publication candidates should still appear in:

```text
/Review/Publications
```

But now the evidence summary should include `PATCH007Evidence` when affiliated institutions or known coauthors are found.

## Important limitation

Known coauthor overlap depends on already accepted publication decisions.

If a faculty member has no accepted publications yet, then known coauthor evidence will be zero for that faculty until publications are accepted.

## Next logical patch

A later patch can make the Publication Review UI display PATCH007 evidence as separate columns instead of only inside `EvidenceSummary`.

Possible next patch:

```text
V3E PATCH008 - Publication Review UI Evidence Columns
```
