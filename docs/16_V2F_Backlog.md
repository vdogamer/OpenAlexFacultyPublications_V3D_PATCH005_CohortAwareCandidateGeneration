# Version 2F Backlog — Candidate Generation and Review Migration

## Goal

After institution, author, and work ingestion are all inside the integrated worker, Version 2F should migrate the candidate-generation and review-decision workflows into the integrated application.

## Planned Additions

- Integrated faculty-author candidate generator
- Integrated faculty-publication candidate generator
- Author match review pages with accept/reject actions
- Publication match review pages with accept/reject actions
- Review decision repositories
- Audit trail pages
- Manager demo dashboard update
- Optional role-protected reviewer/admin pages

## Acceptance Criteria

- `JobName = "FacultyAuthorCandidateGeneration"` works in integrated worker.
- `JobName = "FacultyPublicationCandidateGeneration"` works in integrated worker.
- Review pages can accept/reject candidates.
- Accepted publications report updates from integrated decisions.
- Audit trail works from integrated UI.
