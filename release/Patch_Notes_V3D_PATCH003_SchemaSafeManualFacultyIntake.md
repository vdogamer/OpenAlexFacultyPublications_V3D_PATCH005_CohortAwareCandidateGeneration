# Patch Notes — V3D PATCH003 Schema-Safe Manual Faculty Intake

## Fix

Manual faculty intake no longer assumes a fixed `openalex.Faculty` schema.

## Patched Files

```text
src/OpenAlex.Data/FacultyRepository.cs
src/OpenAlex.Web/Pages/Admin/AddFaculty.cshtml
src/OpenAlex.Web/Pages/Admin/AddFaculty.cshtml.cs
src/OpenAlex.Web/Program.cs
sql/197_ManualFacultyIntake_Schema_Check.sql
docs/47_V3D_PATCH003_Schema_Safe_Manual_Faculty_Intake.md
```

## Result

The `/Admin/AddFaculty` page should load even when optional columns are missing, and inserts should adapt to the actual faculty table.
