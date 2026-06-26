# V3E PATCH013 - Publication Review Filters and Search

## Purpose

PATCH013 adds search and filter controls to `/Review/Publications` so reviewers can focus the pending publication queue as the pilot grows.

## Branch

```text
v3e-patch013-publication-review-filters
```

## Changed files

```text
src/OpenAlex.Data/ReviewQueueRepository.cs
src/OpenAlex.Web/Pages/Review/Publications.cshtml
src/OpenAlex.Web/Pages/Review/Publications.cshtml.cs
```

## Added files

```text
sql/320_V3E_PATCH013_PublicationReviewFilters_Diagnostics_SQL2016.sql
docs/V3E_PATCH013_PublicationReviewFilters.md
```

## New filters

The review page now supports:

```text
Search text
Minimum score
Institution evidence: Any / Present / Missing
Coauthor evidence: Any / Present / Missing
Near-duplicate status: Any / Warning / Resolved / None
Resolution status: Any / Unresolved / Resolved / KeepSeparate / DuplicateOfCandidate / SuppressWarning
```

## Search fields

Search text checks:

```text
faculty display name
department name
publication title
matched author display name
OpenAlex work ID
```

## Safety boundary

PATCH013 does not change scoring, candidate generation, review decisions, or resolution data. It only changes the query and page controls used to display pending review cards.

## How to test

```powershell
git fetch
git checkout v3e-patch013-publication-review-filters
git pull
dotnet build OpenAlexFacultyPublications.sln
```

Run the web app and open:

```text
/Review/Publications
```

Try:

```text
Search = Schiff
Minimum score = 90
Institution evidence = Present
Coauthor evidence = Present
Near-duplicate = Resolved
Resolution = KeepSeparate
```

Then click Reset.

## Diagnostics

Run:

```sql
sql/320_V3E_PATCH013_PublicationReviewFilters_Diagnostics_SQL2016.sql
```

Change the variables at the top of the SQL file to test different filter combinations.
