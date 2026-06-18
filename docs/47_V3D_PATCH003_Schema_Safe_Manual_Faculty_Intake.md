# V3D PATCH003 — Schema-Safe Manual Faculty Intake

## Problem

The previous manual faculty intake patch could fail if the deployed `openalex.Faculty` table did not exactly match the column assumptions in the new page/repository.

## Fix

PATCH003 makes manual faculty intake schema-safe.

It detects actual columns in `openalex.Faculty` and inserts only columns that exist.

Recognized column aliases include:

```text
DisplayName / FacultyDisplayName / FullName / Name
PrimaryEmail / Email
DepartmentName / Department
DivisionName / Division
AppointmentTitle / Title
Orcid / ORCID
ClaimedOpenAlexAuthorId / OpenAlexAuthorId / AuthorOpenAlexId
IsActive / Active
```

It also handles required V1D fields if present:

```text
SourceSystem
SourceFacultyKey
CreatedAtUtc
UpdatedAtUtc
LastSeenAtUtc
```

## Test

Run:

```sql
:r sql/197_ManualFacultyIntake_Schema_Check.sql
```

Then open:

```text
/Admin/AddFaculty
```
