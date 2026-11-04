using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.Web.Auth;
using OpenAlex.Web.Services;

namespace OpenAlex.Tests;

public sealed class DoiSubmissionAuthorizationTests
{
    [Theory]
    [InlineData("Admin", "Disabled")]
    [InlineData("Reviewer", "Disabled")]
    [InlineData("Admin", "RemovedUser")]
    [InlineData("Reviewer", "RemovedUser")]
    [InlineData("Admin", "RemovedRole")]
    [InlineData("Reviewer", "RemovedRole")]
    [InlineData("Admin", "DemotedWithoutAccess")]
    [InlineData("Reviewer", "RevokedAccess")]
    [InlineData("Reviewer", "OtherDepartment")]
    [InlineData("Reviewer", "ReusedUsername")]
    [InlineData("Reviewer", "RenamedUser")]
    public async Task Existing_cookie_cannot_submit_after_live_permission_changes(string cookieRole, string change)
    {
        using var app = new AuthorizationApp(cookieRole);
        using var client = app.NewClient();
        // Real protected auth cookie and real antiforgery token, issued BEFORE revocation.
        var token = await client.GetStringAsync("/__test/session");
        app.Revoke(change);

        using var response = await SubmitAsync(client, token);
        AssertAccessDenied(response);
        Assert.Equal(0, app.Freshness.SubmissionCalls);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Reviewer")]
    public async Task Current_authorized_account_can_submit_and_actor_comes_from_cookie(string role)
    {
        using var app = new AuthorizationApp(role);
        using var client = app.NewClient();
        var token = await client.GetStringAsync("/__test/session");
        using var response = await SubmitAsync(client, token);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Reports/PublicationFreshness", response.Headers.Location!.OriginalString);
        Assert.Equal(1, app.Freshness.SubmissionCalls);
        Assert.Equal(12, app.Freshness.ActorId);
        Assert.Equal("test.reviewer", app.Freshness.ActorName);
        Assert.Equal(18, app.Freshness.DepartmentId);
    }

    [Fact]
    public async Task Revocation_at_database_write_returns_denial_not_success_redirect()
    {
        using var app = new AuthorizationApp("Reviewer");
        using var client = app.NewClient();
        var token = await client.GetStringAsync("/__test/session");
        app.Freshness.RejectAtWrite = true;
        using var response = await SubmitAsync(client, token);
        AssertAccessDenied(response);
        Assert.Equal(1, app.Freshness.SubmissionCalls);
        Assert.Equal(0, app.Freshness.SuccessfulWrites);
    }

    [Fact]
    public async Task Real_Razor_post_still_requires_antiforgery_token()
    {
        using var app = new AuthorizationApp("Admin");
        using var client = app.NewClient();
        await client.GetStringAsync("/__test/session");
        using var response = await SubmitAsync(client, null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.Freshness.SubmissionCalls);
    }

    [Fact]
    public async Task Invalid_signed_account_id_fails_closed()
    {
        using var app = new AuthorizationApp("Admin");
        app.CookieProfile.AppUserId = 0;
        using var client = app.NewClient();
        var token = await client.GetStringAsync("/__test/session");
        using var response = await SubmitAsync(client, token);
        AssertAccessDenied(response);
        Assert.Equal(0, app.Freshness.SubmissionCalls);
    }

    [Fact]
    public async Task Revoked_admin_cookie_cannot_reenable_itself_at_role_management()
    {
        using var app = new AuthorizationApp("Admin");
        using var client = app.NewClient();
        var token = await client.GetStringAsync("/__test/session");
        app.Revoke("RemovedRole");
        using var response = await client.PostAsync("/Admin/Roles?handler=ToggleEnabled", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["id"] = "12", ["enabled"] = "true", ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Reviewer")]
    public async Task Live_department_check_also_denies_disabled_profiles_without_cookie_middleware(string role)
    {
        var store = new LiveStore();
        store.Current!.Roles = new[] { role };
        var principal = ClaimsPrincipalFactory.Create(store.Current, CookieAuthenticationDefaults.AuthenticationScheme);
        store.Current.IsEnabled = false;
        var authorization = new DepartmentReviewAuthorizationService(store);
        Assert.False(await authorization.IsGlobalAdminAsync(principal));
        Assert.False(await authorization.CanReviewDepartmentAsync(principal, 18));
        Assert.Empty(await authorization.GetAllowedDepartmentsAsync(principal));
    }

    [Fact]
    public async Task Demoted_admin_is_department_scoped_even_with_old_admin_claim()
    {
        var store = new LiveStore();
        var cookie = ClaimsPrincipalFactory.Create(store.Current!, CookieAuthenticationDefaults.AuthenticationScheme);
        store.Current!.Roles = new[] { "Reviewer" };
        var authorization = new DepartmentReviewAuthorizationService(store);
        Assert.False(await authorization.IsGlobalAdminAsync(cookie));
        Assert.True(await authorization.CanReviewDepartmentAsync(cookie, 18));
        Assert.False(await authorization.CanReviewDepartmentAsync(cookie, 19));
        Assert.Single(await authorization.GetAllowedDepartmentsAsync(cookie));
        store.Current.IsEnabled = false;
        Assert.Empty(await authorization.GetAllowedDepartmentsAsync(cookie));
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, string? token)
    {
        var form = new Dictionary<string, string>
        {
            ["doi"] = "10.1234/fixture", ["uvaUserId"] = "faculty1", ["departmentId"] = "18",
            // These crafted fields must never replace the server-authenticated actor.
            ["submittedBy"] = "forged.admin", ["submittedByAppUserId"] = "999"
        };
        if (token is not null) form["__RequestVerificationToken"] = token;
        return client.PostAsync("/Reports/PublicationFreshness?handler=SendToReview", new FormUrlEncodedContent(form));
    }

    private static void AssertAccessDenied(HttpResponseMessage response)
    {
        // Preserve this browser application's existing cookie-forbid redirect behavior.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var destination = response.Headers.Location!.OriginalString;
        Assert.True(destination.Contains("/Account/AccessDenied") || destination.Contains("/Account/Login"), destination);
    }

    private sealed class AuthorizationApp : WebApplicationFactory<Program>
    {
        public AppUserRoleProfile CookieProfile { get; } = new()
        {
            AppUserId = 12, UserName = "test.reviewer", IsEnabled = true
        };
        public LiveStore Store { get; } = new();
        public SubmissionStub Freshness { get; } = new();

        public AuthorizationApp(string cookieRole)
        {
            CookieProfile.Roles = new[] { cookieRole };
            Store.Current = new AppUserRoleProfile
            {
                AppUserId = 12, UserName = CookieProfile.UserName, IsEnabled = true,
                Roles = new[] { cookieRole }
            };
        }

        public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost")
        });

        public void Revoke(string change)
        {
            switch (change)
            {
                case "Disabled": Store.Current!.IsEnabled = false; break;
                case "RemovedUser": Store.Current = null; break;
                case "RemovedRole": Store.Current!.Roles = new[] { "Reader" }; break;
                case "DemotedWithoutAccess": Store.Current!.Roles = new[] { "Reviewer" }; Store.CanReview = false; break;
                case "RevokedAccess": Store.CanReview = false; break;
                case "OtherDepartment": Store.DepartmentId = 19; break;
                case "ReusedUsername": Store.Current!.AppUserId = 99; break;
                case "RenamedUser": Store.Current!.UserName = "renamed.user"; break;
                default: throw new ArgumentOutOfRangeException(nameof(change));
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                // Even a regression must never reach an inherited real application DB.
                services.RemoveAll<SqlConnectionFactory>();
                services.AddSingleton(new SqlConnectionFactory(new ConfigurationBuilder().Build(),
                    Options.Create(new DatabaseOptions { ConnectionString = "invalid-test-connection-string" })));
                services.Configure<AuthOptions>(options => options.Mode = "DemoCookie");
                services.RemoveAll<IDepartmentReviewAuthorizationStore>();
                services.AddSingleton<IDepartmentReviewAuthorizationStore>(Store);
                services.RemoveAll<IFacultyPublicationFreshnessService>();
                services.AddSingleton<IFacultyPublicationFreshnessService>(Freshness);
                services.AddSingleton<IStartupFilter>(new CookieBootstrap(CookieProfile));
            });
        }
    }

    // Test-only login middleware. It is never registered in the production app.
    private sealed class CookieBootstrap(AppUserRoleProfile profile) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path != "/__test/session")
                {
                    await nextMiddleware();
                    return;
                }
                context.User = ClaimsPrincipalFactory.Create(profile, CookieAuthenticationDefaults.AuthenticationScheme);
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, context.User);
                var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                await context.Response.WriteAsync(antiforgery.GetAndStoreTokens(context).RequestToken!);
            });
            next(app);
        };
    }

    private sealed class LiveStore : IDepartmentReviewAuthorizationStore
    {
        public AppUserRoleProfile? Current { get; set; } = new()
        {
            AppUserId = 12, UserName = "test.reviewer", IsEnabled = true, Roles = new[] { "Admin" }
        };
        public bool CanReview { get; set; } = true;
        public long DepartmentId { get; set; } = 18;
        public Task<AppUserRoleProfile?> FindUserAsync(string userName, CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task<bool> UserCanReviewDepartmentAsync(long appUserId, long departmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Current?.IsEnabled == true && Current.AppUserId == appUserId &&
                (Current.Roles.Contains("Admin") || (Current.Roles.Contains("Reviewer") && CanReview && departmentId == DepartmentId)));
        public async Task<IReadOnlyList<DepartmentAccessItem>> GetReviewDepartmentsForUserAsync(long appUserId, CancellationToken cancellationToken = default) =>
            await UserCanReviewDepartmentAsync(appUserId, DepartmentId, cancellationToken)
                ? new[] { new DepartmentAccessItem { DepartmentId = DepartmentId, CanReview = true } }
                : Array.Empty<DepartmentAccessItem>();
    }

    private sealed class SubmissionStub : IFacultyPublicationFreshnessService
    {
        public int SubmissionCalls { get; private set; }
        public int SuccessfulWrites { get; private set; }
        public bool RejectAtWrite { get; set; }
        public long ActorId { get; private set; }
        public string? ActorName { get; private set; }
        public long DepartmentId { get; private set; }
        public Task<FacultyPublicationFreshnessResult> ScanByUserIdAsync(string userId, int? requestedFromYear = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PublicationDoiLookupResult> LookupDoiAsync(string doi, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FacultyPublicationManualReviewSubmissionResult> SendDoiToReviewAsync(
            string doi, string uvaUserId, long expectedDepartmentId, string submittedBy, long submittedByAppUserId, CancellationToken cancellationToken = default)
        {
            SubmissionCalls++;
            if (RejectAtWrite) throw new UnauthorizedAccessException("Simulated database-time revocation.");
            ActorId = submittedByAppUserId;
            ActorName = submittedBy;
            DepartmentId = expectedDepartmentId;
            SuccessfulWrites++;
            return Task.FromResult(new FacultyPublicationManualReviewSubmissionResult
            {
                FacultyPublicationReviewCandidateId = 123, PublicationKey = 456, WasCreated = true,
                SubmissionStatus = "CreatedPendingCandidate"
            });
        }
    }
}
