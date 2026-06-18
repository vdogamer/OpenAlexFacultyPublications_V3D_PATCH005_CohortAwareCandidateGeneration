# Runtime Troubleshooting — Version 2B

## Generic Error Page on Every Web Page

If every preview page shows:

```text
Error
An unexpected error occurred.
```

the most likely cause is a database connection-string mismatch.

Version 2B PATCH002 supports both of these config styles:

```json
"Database": {
  "ConnectionString": "Server=localhost;Database=OpenAlexIngestion_Test;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

and:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=OpenAlexIngestion_Test;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

## Show Full Development Error

From PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --urls "http://localhost:5021"
```

Then reload the failing page. ASP.NET Core should show the real exception.

## Common Causes

1. Wrong database name in `appsettings.json`.
2. SQL scripts not run yet.
3. Missing V1H reporting views.
4. Column mismatch between repository and view.
5. App is pointing to an empty database instead of the tested OpenAlex database.
