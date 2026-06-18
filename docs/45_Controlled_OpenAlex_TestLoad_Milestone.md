# Controlled Real OpenAlex TestLoad Milestone

## Purpose

This milestone proves that the app can pull real OpenAlex data in a controlled way and move it through the full workflow:

```text
OpenAlex API
→ SQL Server staging/relational tables
→ faculty-author candidates
→ human review
→ publication candidates
→ accepted publications report
```

## Important 2026 API Update

OpenAlex now uses API keys. The previous `mailto` polite-pool behavior has been replaced.

Set:

```json
"OpenAlex": {
  "ApiKey": "YOUR_OPENALEX_API_KEY"
}
```

Do not commit a real API key to source control.

## Recommended First Real-Data Run

Run in this order:

1. `InstitutionIngestion`
2. `AuthorIngestion`
3. `WorkIngestion`
4. `FacultyAuthorCandidateGeneration`
5. Review author candidates
6. `FacultyPublicationCandidateGeneration`
7. Review publication candidates
8. Confirm accepted publications report

## Safe Initial Limits

```json
"InstitutionIngestion": {
  "RunMode": "TestLoad",
  "MaxPages": 1,
  "MaxRecords": 10
},
"AuthorIngestion": {
  "RunMode": "TestLoad",
  "MaxPages": 1,
  "MaxRecords": 100
},
"WorkIngestion": {
  "RunMode": "TestLoad",
  "MaxPages": 1,
  "MaxRecords": 100
}
```

## Success Criteria

- API key works.
- Institution data loads.
- Author data loads.
- Work/publication data loads.
- Request logs are recorded.
- Operational logs are recorded.
- Faculty-author candidates are generated.
- Review pages work.
- Accepted publications report shows real OpenAlex-derived data.
