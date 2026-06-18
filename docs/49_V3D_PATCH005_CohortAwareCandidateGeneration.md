# V3D PATCH005 — Candidate Generation Honors PilotFacultyCohort

## Problem

During the 15-faculty pilot expansion, the candidate generator produced only a small number of author candidates. The likely cause was that candidate generation loaded the first active faculty rows from `openalex.Faculty` instead of explicitly honoring the 15 enabled rows in `openalex.PilotFacultyCohort`.

## Fix

PATCH005 adds a new setting:

```json
"CandidateGeneration": {
  "PilotCohortName": "Pilot Expansion 15 Faculty"
}
```

When `PilotCohortName` is provided:

- `FacultyAuthorCandidateGeneration` processes the enabled faculty in that cohort, ordered by `PilotOrder`.
- `FacultyPublicationCandidateGeneration` limits publication candidate source rows to accepted author decisions for that same cohort.
- Ingestion run logs include the cohort name in `InstitutionFilter` and summary text.
- A diagnostic SQL script shows cohort readiness and candidate counts per faculty.

## SQL diagnostic

Run:

```sql
:r sql/199_PATCH005_CohortAwareCandidateGeneration_Diagnostic.sql
```

## Required worker settings

```json
"Worker": {
  "Enabled": true,
  "JobName": "FacultyAuthorCandidateGeneration",
  "RunOnceAndStop": true,
  "DelaySeconds": 10
},
"CandidateGeneration": {
  "RunMode": "TestLoad",
  "PilotCohortName": "Pilot Expansion 15 Faculty",
  "MaxFacultyToProcess": 15,
  "MaxAuthorsToScan": 5000,
  "MaxCandidatesPerFaculty": 10,
  "MinimumNameScore": 45,
  "MinimumMatchScore": 55,
  "StartedBy": "OpenAlex.PilotExpansion15",
  "ApplicationVersion": "3D-PATCH005-PilotExpansion15"
}
```

## Expected outcome

The run summary should show:

```text
FacultyProcessed=15; PilotCohortName=Pilot Expansion 15 Faculty
```

If some faculty still have zero candidates after this patch, the generator is processing them correctly, but the OpenAlex author table does not currently contain matching rows for those faculty or the name/affiliation evidence is too weak.
