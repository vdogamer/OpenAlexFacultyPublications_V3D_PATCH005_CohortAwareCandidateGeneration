# V3E PATCH018E — Faculty Profile Handoff

## Goal

Reduce the self-service workflow to:

1. Load publications from Academic Online.
2. Review.
3. Copy.
4. Open the Research Faculty Directory Pubs / HTML editor.
5. Paste and save.

## Research Faculty Directory target

The current faculty profile editor uses the pattern:

`https://apps.med.virginia.edu/ResearchFacultyDirectory/Profile/Edit/HTML.aspx?UserName=<computing-id>`

PATCH018E keeps the base URL configurable through:

```json
"FacultyProfileHandoff": {
  "Enabled": true,
  "PublicationsEditBaseUrl": "https://apps.med.virginia.edu/ResearchFacultyDirectory/Profile/Edit/HTML.aspx",
  "DirectPublishingEnabled": false
}
```

## Identity resolution for the handoff link

The URL builder accepts only a simple validated UVA computing ID.

Resolution order:

1. authenticated `computing_id` claim;
2. UVA email local part;
3. authenticated UVA ePPN local part.

If a safe computing ID cannot be resolved, the Open Profile button is omitted.

## Faculty self-service

Route:

`/Faculty/MyPublications`

After publications are loaded, the page shows:

- Copy My Publications
- Open My Faculty Profile

The second button opens the faculty member's Pubs / HTML editor in a new tab.

## Universal manager lookup

Route:

`/Reports/AcademicOnlineFacultyLookup`

When the selected Academic Online scholar supplies an `@virginia.edu` email address, the page also shows:

- Open Faculty Pubs / HTML

The Research Faculty Directory remains responsible for authorization.

## Direct publishing

Direct publishing is intentionally disabled.

The legacy Pubs / HTML editor is ASP.NET Web Forms and uses request state such as VIEWSTATE/EVENTVALIDATION. The currently available source does not establish a supported server API for updating publication HTML.

PATCH018E therefore defines the configuration boundary but does not automate a Web Forms POST.

Before enabling direct publishing, obtain/confirm one of:

- a supported Research Faculty Directory API;
- a database/service-layer write contract approved by the application owner;
- a replacement profile-management endpoint designed for programmatic publication updates.
