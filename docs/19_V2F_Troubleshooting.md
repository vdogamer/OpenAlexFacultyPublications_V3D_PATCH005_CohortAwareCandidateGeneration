# Version 2F Troubleshooting

## No author candidates generated

Check:

- `openalex.Faculty` has active rows.
- `openalex.Author` has rows.
- `openalex.AuthorLastKnownInstitution` has rows.
- `CandidateGeneration:MinimumMatchScore` is not too high.

For testing, lower:

```json
"MinimumMatchScore": 50
```

## No publication candidates generated

You must accept at least one author candidate first.

Check:

```sql
SELECT *
FROM openalex.FacultyAuthorMatchDecision
WHERE DecisionStatus = 'Accepted'
  AND IsCurrent = 1;
```

Also check:

```sql
SELECT TOP 20 *
FROM openalex.WorkAuthorship;
```

## Review button error

Run the web app in Development mode:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --urls "http://localhost:5025"
```

Then reload the page to see the real exception.

## Accepted publications report is empty

Check whether accepted publication decisions exist:

```sql
SELECT *
FROM openalex.FacultyPublicationDecision
WHERE DecisionStatus = 'Accepted'
  AND IsCurrent = 1;
```

Then confirm the V1H reporting views are installed.
