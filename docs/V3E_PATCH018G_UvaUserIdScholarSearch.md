# V3E PATCH018G — Search Academic Online by UVA User ID

## Goal

Extend the universal UVA Academic Online publication lookup so managers can search by either:

- faculty name; or
- UVA User ID / computing ID.

Route:

`/Reports/AcademicOnlineFacultyLookup`

## User ID behavior

Accepted input examples:

- `cf2s`
- `cf2s@virginia.edu`

The value is normalized to the UVA email form:

`cf2s@virginia.edu`

The service first searches Academic Online using the full UVA email address. If no exact match is returned, it retries using the normalized computing ID.

In either case, the final candidate list requires an exact case-insensitive email match to:

`<userid>@virginia.edu`

This keeps User ID search identity-safe and avoids fuzzy or similarly named matches.

## Name search

Existing name search behavior is unchanged:

- full-text Academic Online scholar search;
- optional department input;
- ranked candidate list;
- manager chooses the appropriate scholar.

If both faculty name and User ID are entered, User ID takes priority.

## Publication workflow

Once a scholar is selected, all existing PATCH018 features remain available:

- complete publication pagination;
- year filters;
- verified-only filter;
- keyword/topic ANY/ALL filtering;
- copy formatted publications;
- copy HTML source;
- standalone HTML;
- Research Faculty Directory Pubs / HTML handoff when a UVA email is available.

## Database impact

None.
