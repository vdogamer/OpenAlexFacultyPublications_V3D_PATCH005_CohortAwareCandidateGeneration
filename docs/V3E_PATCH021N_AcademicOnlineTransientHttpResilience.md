# V3E PATCH021N — Academic Online Transient HTTP Resilience

## Evidence

PATCH021M runs 21 and 22 failed before completion because the Academic Online
service connection failed transiently.

Run 21:
- connection attempt failed to profile.api.academiconline.edu:443.

Run 22:
- the remote host forcibly closed an established connection.

The failures occurred during Academic Online HTTP calls, not in the local
publication-year policy logic.

Run 22 progressed far enough to touch partial non-current department/roster rows,
but the snapshot safety gate prevented promotion. The previously promoted
Neurosurgery snapshot remains authoritative.

## Goal

Reduce unnecessary failed department refreshes caused by short-lived Academic
Online network/service interruptions without hiding persistent failures.

## Retry policy

AcademicOnlineApiClient now retries transient failures only.

Maximum attempts:
- 4 total attempts (initial + up to 3 retries)

Backoff:
- 1 second
- 2 seconds
- 4 seconds

Retry-After:
- honored when supplied by the server, capped at 30 seconds

Retryable HTTP status codes:
- 408 Request Timeout
- 429 Too Many Requests
- 500 Internal Server Error
- 502 Bad Gateway
- 503 Service Unavailable
- 504 Gateway Timeout

Retryable transport failures:
- HttpRequestException with no HTTP status code
- TaskCanceledException when the caller cancellation token was not cancelled

Non-transient 4xx responses such as 400, 401, 403, and 404 are not retried.

## Request safety

Each retry creates a fresh HttpRequestMessage.

POST requests recreate JsonContent for each attempt, so request bodies are not
reused after disposal.

Caller cancellation is always respected.

## Deployment

No SQL migration is required.

1. Pull PATCH021N.
2. Build the solution.
3. Stop the durable refresh service.
4. Deploy updated worker/client binaries.
5. Start the service.
6. Mark Neurosurgery Due Now.
7. Restart the service for an immediate pass.
8. Confirm a completed 15/15 run.
9. Run sql/448_V3E_PATCH021M_LocalPublicationYearPolicy_Validation_SQL2016.sql.

## Expected result

A normal successful run should remain:

- ScholarsDiscovered = 15
- ScholarsProcessed = 15
- IsCacheComplete = 1

PATCH021M validation should show:

- CurrentReportNullYears = 0
- CurrentReportInvalidYears = 0
- CurrentReportOutsideYearPolicy = 0
- CurrentDepartmentRowsOutsideYearPolicy = 0
- CurrentRosterRowsOutsideYearPolicy = 0

If Academic Online remains unavailable longer than the bounded retry window, the
run still fails explicitly and the previously promoted snapshot remains in place.
