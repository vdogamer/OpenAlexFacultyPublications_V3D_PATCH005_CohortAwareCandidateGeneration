# V3D PATCH002 — Manual Faculty Intake

## Problem

The `/Faculty` page listed faculty but did not allow adding new faculty rows. This blocked the 15-faculty pilot expansion because the database still had only 5 faculty rows.

## Fix

PATCH002 adds:

```text
/Admin/AddFaculty
```

The page allows an Admin user to add real faculty rows with:

- Display name
- Primary email
- Department
- Division
- Appointment title
- ORCID
- Claimed OpenAlex Author ID
- IsActive

## Recommended workflow

1. Login as `admin.demo`.
2. Open `/Admin/AddFaculty`.
3. Add 10 more actual faculty.
4. Confirm:

```sql
SELECT COUNT(*) AS TotalFaculty FROM openalex.Faculty;
```

5. When the count reaches 15, rerun the pilot cohort scripts:

```sql
:r sql/190_PilotExpansion_Create_Cohort_Table.sql
:r sql/191_PilotExpansion_Select_15_Actual_Faculty_TEMPLATE.sql
:r sql/192_PilotExpansion_Validate_15_Faculty.sql
```

6. Then run `FacultyAuthorCandidateGeneration`.

## Fallback

If the app page cannot be used, use:

```text
sql/196_ManualFaculty_Insert_Template.sql
sql/197_ManualFaculty_Count_And_Cohort_NextStep.sql
```
