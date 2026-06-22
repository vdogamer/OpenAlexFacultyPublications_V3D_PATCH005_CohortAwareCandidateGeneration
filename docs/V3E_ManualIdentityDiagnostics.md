# V3E Manual Identity Diagnostics

## Purpose

This diagnostic pass captures evidence for faculty identity disambiguation before changing the matching algorithm.

The point is not to decide automatically yet. The point is to prove what the next patch must handle:

1. A faculty member can map to multiple plausible OpenAlex author IDs.
2. Institution-only author discovery can miss early-career work.
3. ORCID is strong evidence when present, but it is too sparse to be the only discovery path.
4. Awards can be discoverable by first/last name even when identifier-based searches are empty.
5. Repeated awards may be true repeats, renewals, supplements, or duplicate records and must be grouped carefully.

## Default test cases

### Susan M. Pollart

Manager-observed issue: Susan M. Pollart appears to have multiple OpenAlex author IDs.

Diagnostic goal:

- Search OpenAlex authors by multiple name forms.
- Capture all plausible author IDs.
- Pull a sample of works for each candidate author ID.
- Compare institutions, publication dates, and work titles.

Expected V3E design implication:

- Do not treat one OpenAlex author ID as the complete faculty identity.
- Store multiple candidate author IDs per faculty.
- Later allow human review to accept, reject, or merge candidate IDs into one faculty identity set.

### Jeffrey Martens

Manager-observed issue: award search by ORCID produces zero results, while first/last name search returns many records.

Diagnostic goal:

- Search awards by free-text name search.
- Search awards by lead investigator given/family name filters.
- Capture funder, award ID, date range, amount, lead investigator, ORCID if available, and provenance.
- Build a basic dedupe fingerprint for grouping repeated awards.

Expected V3E design implication:

- Award discovery must not rely only on ORCID.
- Awards need a reviewable grouping layer, not blind deduplication.

## How to run

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\OpenAlexIdentityDiagnostics.ps1
```

Recommended with an API key:

```powershell
$env:OPENALEX_API_KEY = "your-key-here"
powershell -ExecutionPolicy Bypass -File .\tools\OpenAlexIdentityDiagnostics.ps1
```

Optional parameters:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\OpenAlexIdentityDiagnostics.ps1 `
  -OutputRoot "artifacts/openalex-identity-diagnostics" `
  -PerPage 50 `
  -WorkSamplePerAuthor 20
```

## Output files

The script writes files under:

```text
artifacts/openalex-identity-diagnostics/<timestamp>/
```

Important files:

```text
author-candidates.csv
author-work-samples.csv
award-candidates.csv
diagnostic-summary.json
```

Raw API responses are also retained:

```text
author-search-*.json
works-by-author-*.json
award-search-*.json
```

## What to inspect first

### 1. Susan M. Pollart author candidates

Open:

```text
author-candidates.csv
```

Look for:

- How many unique `AuthorOpenAlexShortId` values appear.
- Whether one or more candidate IDs have UVA-related institutions.
- Whether older or lower-work-count profiles look like fragments rather than different people.
- Whether ORCID is missing, inconsistent, or only present on one profile.

Then open:

```text
author-work-samples.csv
```

Look for:

- Works that clearly belong to Susan M. Pollart.
- Works that appear to belong to a different Susan/S. Pollart.
- Works before UVA affiliation.
- Works with institutions outside UVA that should still belong to the same faculty member.

### 2. Jeffrey Martens awards

Open:

```text
award-candidates.csv
```

Look for:

- Number of rows returned by name-based methods.
- Whether lead investigator ORCID is empty.
- Whether the same funder award ID appears multiple times.
- Whether title/funder/date/amount suggest duplicate rows, renewals, or distinct awards.

## Acceptance criteria for V3E PATCH001

V3E PATCH001 should be considered successful when the app can produce a reviewable identity candidate set like this:

```text
FacultyId
FacultyDisplayName
DiscoveryMethod
CandidateAuthorOpenAlexId
CandidateDisplayName
CandidateOrcid
WorksCount
CitedByCount
LastKnownInstitutions
EvidenceScore
EvidenceStatus
EvidenceSummary
EvidenceJson
```

For Susan M. Pollart, the app should surface all plausible author IDs discovered through name searches, not just the one attached to a UVA institution.

For Jeffrey Martens, the award-discovery design should preserve name-based award results even when ORCID-based discovery is empty.

## Design warning

Do not auto-merge OpenAlex author profiles just because names match.

The safer workflow is:

```text
Faculty -> Identity Candidates -> Evidence Expansion -> Human Review -> Accepted Identity Set -> Publications/Awards
```

This protects the app from both false positives and false negatives.
