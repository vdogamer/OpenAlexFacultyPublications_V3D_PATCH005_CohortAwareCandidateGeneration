# Patch Notes — Version 2B PATCH001

## Fix

Corrected `src/OpenAlex.Reporting/CsvExportService.cs`.

The original V2B package generated an invalid C# string-escaping line in the CSV helper method, causing build errors such as:

- CS1002
- CS1513
- CS8997 Unterminated raw string literal

## Corrected Method

```csharp
private static string Csv(string? value)
{
    value ??= string.Empty;
    return "\"" + value.Replace("\"", "\"\"") + "\"";
}
```

## Recommended Rebuild

```powershell
dotnet clean
dotnet restore
dotnet build
```
