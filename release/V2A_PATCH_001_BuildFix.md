# V2A PATCH 001 — Build Fix

## Issue

`dotnet build` failed in `OpenAlex.Data` with:

```text
CS1503: Argument 2: cannot convert from 'Microsoft.Extensions.Configuration.IConfigurationSection' to 'System.Action<OpenAlex.Data.DatabaseOptions>'
```

## Cause

The shared projects used:

```csharp
services.Configure<TOptions>(configuration.GetSection("..."));
```

but the class library projects did not reference the package that supplies this overload.

## Fix

Added:

```xml
<PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
```

to:

- `src/OpenAlex.Data/OpenAlex.Data.csproj`
- `src/OpenAlex.OpenAlexClient/OpenAlex.OpenAlexClient.csproj`
- `src/OpenAlex.OperationalLogging/OpenAlex.OperationalLogging.csproj`
- `src/OpenAlex.Worker/OpenAlex.Worker.csproj`

## Rebuild

From the solution root:

```powershell
dotnet clean
dotnet restore
dotnet build
```
