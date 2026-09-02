# V3E PATCH018D — Universal UVA Academic Online Faculty Lookup

## Goal

Provide a manager/demo page that can search the University of Virginia Academic Online tenant directly by faculty name and generate a complete publication HTML page without requiring a local FacultyId or an existing durable mapping.

Route:

`/Reports/AcademicOnlineFacultyLookup`

## Workflow

1. Enter a UVA faculty member's name.
2. Optionally enter a department.
3. Academic Online returns matching scholars from the UVA tenant.
4. Choose the correct scholar.
5. Retrieve the scholar's general profile.
6. Retrieve all publication pages through the PATCH018 pagination client.
7. Render publication HTML.
8. Copy formatted publications, copy HTML source, or open standalone HTML.

## Identity safety

PATCH018D is deliberately read-only with respect to production identity mappings.

Selecting a scholar on the universal lookup page does **not**:

- create a `FacultyAcademicOnlineAuthorMap` row;
- replace a current mapping;
- change a faculty record;
- alter publication review decisions.

The production self-service page remains:

`/Faculty/MyPublications`

and continues to require exact signed-in faculty resolution plus a verified durable Academic Online mapping.

## Scope

The universal lookup can retrieve scholars that are present in the UVA Academic Online tenant even if they are not represented in the current local `openalex.Faculty` table.

It cannot retrieve Academic Online publications for a UVA faculty member who has no Academic Online scholar record.

## Runtime secret

The Web process must have:

`AcademicOnline__ApiKey`

available in its environment.

## Test

Build and launch the Web app, then open:

`/Reports/AcademicOnlineFacultyLookup`

Search for a known UVA faculty member, choose the appropriate scholar, and verify:

- profile information;
- publications returned;
- pages fetched;
- copy functionality;
- standalone HTML.
