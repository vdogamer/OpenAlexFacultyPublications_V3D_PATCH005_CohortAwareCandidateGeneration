# Version 2F Candidate Generation + Review Workflow

## What Changed

Version 2F adds the review workflow that sits after ingestion.

## Worker Jobs

```text
FacultyAuthorCandidateGeneration
FacultyPublicationCandidateGeneration
```

## Web Review Actions

```text
/Review/Authors
/Review/Publications
```

Each review page now has:

- Accept button
- Reject button
- optional reviewer notes field

## Author Review Logic

Accepting an author candidate:

1. Supersedes prior current decisions for the same faculty/author pair.
2. Inserts a current `Accepted` row into `openalex.FacultyAuthorMatchDecision`.
3. Marks the candidate as `AutoAcceptedCandidate`.

Rejecting an author candidate:

1. Supersedes prior current decisions for the same faculty/author pair.
2. Inserts a current `Rejected` row.
3. Marks the candidate as `AutoRejectedCandidate`.

## Publication Review Logic

Accepting a publication candidate:

1. Supersedes prior current decisions for the same faculty/work pair.
2. Inserts a current `Accepted` row into `openalex.FacultyPublicationDecision`.
3. Marks the candidate as `AutoAcceptedCandidate`.

Rejecting a publication candidate:

1. Supersedes prior current decisions for the same faculty/work pair.
2. Inserts a current `Rejected` row.
3. Marks the candidate as `AutoRejectedCandidate`.

## Important Safety Note

Version 2F does not yet add role-based authorization. The reviewer name defaults to:

```text
local.demo.reviewer
```

Version 2G should integrate authentication/roles from V1N into the V2 web app.
