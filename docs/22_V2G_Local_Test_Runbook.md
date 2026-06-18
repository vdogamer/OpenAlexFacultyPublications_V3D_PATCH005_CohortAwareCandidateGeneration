# Version 2G Local Test Runbook

## 1. Confirm auth tables

Run:

```sql
:r sql/100_V2G_Auth_Role_Readiness_Check.sql
:r sql/102_V2G_Demo_User_Verification.sql
```

If demo users are missing, run the V1N auth seed scripts.

## 2. Run web app

```powershell
cd src/OpenAlex.Web
copy appsettings.example.json appsettings.json
dotnet clean
dotnet restore
dotnet build
dotnet run --urls "http://localhost:5026"
```

## 3. Login as Reader

Open:

```text
/Account/Login
```

Login:

```text
manager.demo
```

Expected:

- Can view reports.
- Cannot access review pages.
- Cannot access admin pages.

## 4. Login as Reviewer

Login:

```text
reviewer.demo
```

Expected:

- Can open author/publication review.
- Can accept/reject candidates.
- Cannot open admin pages.

## 5. Login as Admin

Login:

```text
admin.demo
```

Expected:

- Can open admin pages.
- Can open operations page.
- Can review candidates.

## 6. Audit authentication events

Open:

```text
/Admin/AuthEvents
```

Or run:

```sql
:r sql/101_V2G_Auth_Audit_Queries.sql
```
