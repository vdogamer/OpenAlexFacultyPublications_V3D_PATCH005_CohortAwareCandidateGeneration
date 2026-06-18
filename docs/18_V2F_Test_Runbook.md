# Version 2F Test Runbook

## 1. Confirm prerequisites

Run:

```sql
:r sql/090_V2F_Candidate_Review_Readiness_Check.sql
```

Make sure these counts are nonzero:

- Faculty
- Author
- AuthorLastKnownInstitution
- Work
- WorkAuthorship

## 2. Run author candidate generation

Set:

```json
"Worker": {
  "Enabled": true,
  "JobName": "FacultyAuthorCandidateGeneration",
  "RunOnceAndStop": true
}
```

Run the worker.

## 3. Review author candidates

Start the web app and open:

```text
/Review/Authors
```

Accept at least one strong author candidate.

## 4. Run publication candidate generation

Set:

```json
"Worker": {
  "Enabled": true,
  "JobName": "FacultyPublicationCandidateGeneration",
  "RunOnceAndStop": true
}
```

Run the worker.

## 5. Review publication candidates

Open:

```text
/Review/Publications
```

Accept at least one publication candidate.

## 6. Confirm accepted publications

Open:

```text
/Reports/AcceptedPublications
```

Then run:

```sql
:r sql/092_V2F_Review_Decision_Audit_Queries.sql
```
