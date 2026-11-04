# OpenAlex.Tests

Run:

```powershell
dotnet test tests\OpenAlex.Tests\OpenAlex.Tests.csproj
```


## PATCH001

`GlobalUsings.cs` imports xUnit globally for the test project.

## DOI submission authorization

The test project now references the actual Web project. The authorization tests
use ASP.NET Core's in-memory test host with real protected cookies, real Razor
POST routing and antiforgery, but a replaceable live-account store and submission
service. They do not connect to an application database or OpenAlex.

Cookies and antiforgery tokens are issued before disabling/removing accounts,
revoking roles/access, or changing account identity. Tests verify denied writes,
current Admin/Reviewer access, signed actor propagation despite crafted form
fields, rejection at write time, stale Admin denial at role management, and
continued antiforgery enforcement. The session-bootstrap middleware exists only
inside the test factory; it is not registered in the production application.

Run `tests/sql/DoiSubmissionAuthorization_Regression.sql` separately in a new
scratch SQL Server database to exercise the real stored-procedure boundary.
See `docs/DoiSubmissionLiveAuthorization.md` for deployment and validation limits.
