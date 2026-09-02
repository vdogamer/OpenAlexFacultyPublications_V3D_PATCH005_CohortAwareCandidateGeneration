# V3E PATCH018F — Publication Keyword Filtering

## Goal

Allow faculty and managers to narrow Academic Online publication lists by topic before generating/copying publication HTML.

Routes:

- `/Faculty/MyPublications`
- `/Reports/AcademicOnlineFacultyLookup`

## Matching fields

The keyword filter matches case-insensitively against:

1. publication title;
2. Academic Online `keywords` payload;
3. venue / journal name.

Coauthor names are intentionally not included in the topic filter.

## Multiple terms

Terms may be separated with commas or semicolons.

Example:

`cancer, genomics`

Modes:

- `ANY`: at least one term must match;
- `ALL`: every term must match somewhere in the title/keywords/venue search text.

The implementation normalizes duplicate terms and limits one request to the first 20 distinct terms.

## Pipeline order

Academic Online remains the source of the full paginated publication set.

Filtering order:

1. Academic Online author/publication retrieval;
2. Academic Online verified/year filters;
3. PATCH018F local keyword filter;
4. HTML generation;
5. copy/profile handoff.

PATCH018F does not invent or depend on an undocumented Academic Online keyword-query parameter.

## Output consistency

The same filtered `AcademicOnlineFacultyPublicationsResult.Publications` list feeds:

- visible preview;
- publication count;
- rich clipboard output;
- raw HTML output;
- standalone generated HTML.

Therefore the user copies exactly the publications shown after filtering.

## Result metadata

The result retains:

- `UnfilteredPublicationCount`;
- normalized `KeywordQuery`;
- `KeywordMatchMode`;
- parsed `KeywordTerms`.

This allows the UI to show, for example:

`18 matching publications from 148 before keyword filtering`.

## Database impact

None.

PATCH018F adds no SQL objects and changes no faculty mappings, publication decisions, or Academic Online snapshots.
