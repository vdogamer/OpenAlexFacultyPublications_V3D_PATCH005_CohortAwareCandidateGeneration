# Patch Notes — V3D PATCH005 Cohort-Aware Candidate Generation

## Purpose

Force candidate generation to honor `openalex.PilotFacultyCohort` for the 15-faculty pilot expansion.

## Patched Files

```text
src/OpenAlex.Data/CandidateGenerationRepository.cs
src/OpenAlex.Worker/CandidateGenerationOptions.cs
src/OpenAlex.Worker/Jobs/FacultyAuthorCandidateGenerationJob.cs
src/OpenAlex.Worker/Jobs/FacultyPublicationCandidateGenerationJob.cs
src/OpenAlex.Worker/appsettings.example.json
sql/199_PATCH005_CohortAwareCandidateGeneration_Diagnostic.sql
docs/49_V3D_PATCH005_CohortAwareCandidateGeneration.md
```

## New Setting

```json
"PilotCohortName": "Pilot Expansion 15 Faculty"
```

## Result

The author candidate generator now processes the enabled faculty in the pilot cohort instead of relying on a generic `TOP (@MaxFacultyToProcess)` query from `openalex.Faculty`.
