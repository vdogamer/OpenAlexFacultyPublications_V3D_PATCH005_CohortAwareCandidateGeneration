# Version 2E Troubleshooting

## Error: CK_IngestionRun_RunMode

Use one of:

```text
SmokeTest
TestLoad
FullLoad
```

Version 2E defaults to `TestLoad`.

## Error: Invalid object name openalex.Work

Run the V1B author/work schema first:

```sql
:r 003_CreateOpenAlexSchema_V1B_AuthorWork.sql
```

## Error: Invalid object name openalex.WorkAuthorshipInstitution

Run the V1B schema first.

## Error: no works returned

Check the configured institution IDs and filter:

```text
authorships.institutions.id:I4210107925|I4210119250|I4210134211
```

Also confirm OpenAlex has works where authorship institutions match those IDs.

## Error: foreign key violation on WorkMatchedInstitution

Confirm the worker successfully inserted/updated the `openalex.Work` row before inserting matched institutions.

## Slow run

Keep these small while testing:

```json
"WorkIngestion": {
  "MaxPages": 1,
  "MaxRecords": 100
}
```

## Generic worker failure

Inspect:

```sql
:r sql/081_V2E_Work_Ingestion_Demo_Queries.sql
```

Also check:

```sql
SELECT TOP 50 *
FROM openalex.OperationalLog
WHERE Category IN ('WorkIngestion', 'Worker')
ORDER BY OperationalLogId DESC;
```
