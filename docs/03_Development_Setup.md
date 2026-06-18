# Development Setup

## Required Tools

- .NET 8 SDK
- SQL Server 2016 or SQL Server 2019
- SQL Server Management Studio
- Visual Studio 2022 or VS Code
- Git

## Recommended Local Folder

```text
C:\Users\Cameron\OneDrive - University of Virginia\Desktop\NewSmokeTest\OpenAlexFacultyPublications_V2A_IntegratedApplication
```

## Open the Solution

Open:

```text
OpenAlexFacultyPublications.sln
```

Or build from command line:

```bash
dotnet build
```

## Configure the Web App

```bash
cd src/OpenAlex.Web
copy appsettings.example.json appsettings.json
```

Edit the connection string.

## Configure the Worker

```bash
cd src/OpenAlex.Worker
copy appsettings.example.json appsettings.json
```

Edit the connection string and job settings.

## Run Web App

```bash
dotnet run --urls "http://localhost:5020"
```

## Run Worker

```bash
dotnet run
```

## Port Conflict Fix

```powershell
netstat -ano | findstr :5020
taskkill /PID <PID> /F
```
