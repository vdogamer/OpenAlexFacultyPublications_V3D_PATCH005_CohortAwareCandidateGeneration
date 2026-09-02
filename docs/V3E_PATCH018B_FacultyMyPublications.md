# V3E PATCH018B — Faculty Self-Service My Publications

## Goal

Provide a faculty-facing Academic Online publication page that requires no FacultyId or Academic Online author ID.

Route:

`/Faculty/MyPublications`

## Production identity resolution

The page uses the authenticated claims already created by the application:

- `ClaimTypes.Email`
- `computing_id`
- authenticated username/ePPN

It resolves to `openalex.Faculty` using exact identity matches only:

1. exact `PrimaryEmail` = authenticated email;
2. exact `PrimaryEmail` = authenticated ePPN when the username is email-shaped;
3. exact `PrimaryEmail` = `<computingId>@virginia.edu`;
4. exact `SourceFacultyKey` = computing ID or username, when that faculty column exists.

There is deliberately no fuzzy display-name match. Zero or ambiguous matches fail safely.

After faculty resolution, the page requires the current verified
`openalex.FacultyAcademicOnlineAuthorMap` mapping created by PATCH018.

## Faculty experience

The page exposes only:

- optional From year;
- optional To year;
- Academic Online verified-only checkbox;
- Refresh My Publications;
- Copy for Profile Editor;
- Copy HTML Source;
- publication preview.

No FacultyId, Academic Online author ID, mapping source, pagination count, or other manager/debug controls are exposed.

## Local DemoCookie test

PATCH018B includes:

`sql/409_V3E_PATCH018B_LocalDemoFacultyLoginSeed_SQL2016.sql`

This is local/test only. It creates or refreshes the demo username:

`faculty.demo.farber`

with the same email as the mapped FacultyId 16 record and grants the Reader role.

Then run:

`sql/410_V3E_PATCH018B_FacultySelfServiceIdentity_Diagnostics_SQL2016.sql`

Expected final action:

`LoginAsFacultyDemoAndOpenMyPublications`

Log out of the current DemoCookie account and log in with:

`faculty.demo.farber`

Then open:

`/Faculty/MyPublications`

## Production deployment

Do not use the PATCH018B demo login seed in production.

With `Auth:Mode=ShibbolethHeaders`, the existing authentication middleware supplies the real UVA email/computing-id claims. The same self-service page then resolves the signed-in faculty member automatically.
