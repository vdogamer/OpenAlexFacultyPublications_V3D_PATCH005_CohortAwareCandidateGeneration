# V3E PATCH018H — Shared Publications by UVA User ID

## Goal

Allow managers to enter multiple UVA User IDs and find publications shared by every selected faculty member, including faculty who are not preloaded in `openalex.Faculty`.

Route:

`/Reports/SharedPublications`

## Identity resolution

Each User ID is normalized using the existing PATCH018G rules.

The service searches Academic Online by:

`<userid>@virginia.edu`

and requires an exact case-insensitive email match. If no exact scholar exists, the User ID is reported as NotFound. If multiple distinct scholar profiles have the exact email, the result is Ambiguous and the comparison stops.

No database faculty row or production mapping is created.

## Publication intersection

For each resolved scholar, the service loads the complete Academic Online publication collection.

A publication is considered shared using this matching priority:

1. OpenAlex ID
2. DOI
3. Academic Online publication ID
4. normalized title + publication year fallback

The result is returned only when the publication can be matched for every selected User ID.

## Safety and data behavior

- ReaderPolicy protected.
- Read-only.
- Maximum 25 User IDs per search.
- Optional Verified-only filter.
- No writes to `openalex.Faculty` or mapping tables.
- Any unresolved or ambiguous User ID prevents a misleading partial intersection.
