# V3E PATCH009A - Publication Review Evidence Card Layout

## Purpose

PATCH009A improves the `/Review/Publications` page layout.

PATCH009 made institution and coauthor evidence visible as table columns. That worked, but the page became too wide and dense for review or manager demos.

PATCH009A replaces the wide table with review cards.

## Branch

```text
v3e-patch009a-publication-review-cards
```

## What changed

PATCH009A modifies:

```text
src/OpenAlex.Web/Pages/Review/Publications.cshtml
src/OpenAlex.Web/wwwroot/css/site.css
```

## New layout

Each publication candidate now appears as a card with:

```text
Candidate status
Match score
Evidence boost
Publication title
Faculty name
Matched author name
Publication year
Department
DOI/OpenAlex link
Institution evidence panel
Coauthor evidence panel
Author match evidence panel
Accept/reject actions
```

## Evidence panels

Institution evidence shows:

```text
match count
evidence weight
matched institution names
```

Coauthor evidence shows:

```text
known coauthor overlap count
prior accepted work count
known coauthor names
```

Author match evidence shows:

```text
matched author name
shortened author-match evidence summary
```

## Why this is better

The card layout makes each candidate self-contained and easier to explain:

```text
Who is the faculty member?
What is the publication?
Why does the system think it matches?
What action should the reviewer take?
```

This is better for daily review and for manager demonstrations.

## How to test

Checkout:

```powershell
git fetch
git checkout v3e-patch009a-publication-review-cards
git pull
```

Build:

```powershell
dotnet build OpenAlexFacultyPublications.sln
```

Run the web app and open:

```text
/Review/Publications
```

## Expected result

The review page should no longer show a wide table.

It should show stacked cards. Each card should show institution/coauthor evidence as badges or short evidence panels.

## Notes

This patch is UI-only.

It does not change matching logic, candidate generation, scoring, or review decisions.
