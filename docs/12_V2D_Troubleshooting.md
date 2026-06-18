# Version 2D Troubleshooting

## Error: CK_IngestionRun_RunMode

Use one of:

```text
SmokeTest
TestLoad
FullLoad
```

Version 2D defaults to `TestLoad`.

## Error: Invalid object name openalex.Author

Run the V1B author/work schema first:

```sql
:r 003_CreateOpenAlexSchema_V1B_AuthorWork.sql
```

## Error: Invalid object name openalex.AuthorLastKnownInstitution

Run the V1B schema first.

## Error: no authors returned

Check the configured institution IDs and filter:

```text
last_known_institutions.id:I4210107925|I4210119250|I4210134211
```

Also confirm OpenAlex has authors with those last-known institutions.

## Error page in web app after running author ingestion

Run the web app in Development mode:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --urls "http://localhost:5023"
```

Then inspect the real exception.
