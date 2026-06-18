# Patch Notes — V3D PATCH002 Manual Faculty Intake

## New

- `/Admin/AddFaculty`
- `ManualFacultyInput`
- `FacultyRepository.AddFacultyAsync`
- Add Faculty link on `/Faculty`
- SQL fallback manual insert template
- count/cohort next-step SQL

## Why

The 15-faculty pilot expansion requires 15 actual faculty rows in `openalex.Faculty`. The existing `/Faculty` page displayed faculty but did not allow manually adding them.

## Safety

The new Add Faculty page requires Admin role.

The page prevents blank display names and attempts to block exact duplicate display-name/email combinations.
