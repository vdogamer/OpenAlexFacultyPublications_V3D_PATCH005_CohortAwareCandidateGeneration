# Release Notes — Version 2F

## Release Name

Candidate Generation + Review Workflow Migration

## Release Type

Integrated workflow migration release.

## New

- `CandidateGenerationRepository`
- `ReviewDecisionRepository`
- candidate-generation data models
- `CandidateGenerationOptions`
- `FacultyAuthorCandidateGenerationJob`
- `FacultyPublicationCandidateGenerationJob`
- Worker routes for candidate-generation jobs
- Review buttons on author queue
- Review buttons on publication queue
- V2F validation/demo/audit SQL scripts
- V2G auth/roles backlog

## Safety

Worker remains disabled by default.

Review actions are demo/local actions until Version 2G adds integrated role enforcement.
