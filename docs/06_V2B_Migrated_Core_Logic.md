# Version 2B Migrated Core Logic

## What Was Migrated

Version 2B begins the move from separate V1 packages into the integrated solution.

### Migrated into OpenAlex.Data

- Dashboard table/view counts
- Faculty list repository
- Author review queue repository
- Publication review queue repository
- Accepted publication reporting repository
- Operational log repository

### Migrated into OpenAlex.Web

- Dashboard page
- Faculty page
- Author review page
- Publication review page
- Accepted publications report
- CSV export endpoint
- Operations log page

### Migrated into OpenAlex.Worker

- Safe institution-ingestion preview job
- Operational logging from worker startup/job execution

## What Is Still Pending

The following should move in Version 2C:

- Institution upsert logic
- Author upsert logic
- Work upsert logic
- IngestionRun creation/completion
- RequestLog creation/completion
- Full cursor paging loop
- Retry/backoff handling
- Background job registration/run tables from V1M

## Why Preview First?

The integrated worker calls OpenAlex and logs what it sees, but does not write database rows. This protects the test database while validating the integrated host, dependency injection, configuration, OpenAlex API client, and operational logging.
