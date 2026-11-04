using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Web.Pages.Reports;
using OpenAlex.Web.Services;

namespace OpenAlex.Tests;

public sealed class FacultyDoiPageHttpTests
{
    [Fact]
    public async Task RendersSearchPageAndNavigationWithoutQueryingOnInitialVisit()
    {
        var repository = new MemoryRepository();
        await using var host = await PageHost.StartAsync(repository);
        var response = await host.Client.GetAsync("/Reports/FacultyDois");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Find DOIs", html);
        Assert.Contains("href=\"/Reports/FacultyDois\"", html);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task RendersDeduplicatedDoisAndEncodesStoredText()
    {
        var repository = new MemoryRepository(
            new("Faculty <script>alert(1)</script>", "10.1234/test", "Title <script>alert(2)</script>", 2026, "Stored"),
            new("Faculty <script>alert(1)</script>", "https://doi.org/10.1234/TEST", "Title", 2025, "Stored"));
        await using var host = await PageHost.StartAsync(repository);
        var response = await host.Client.GetAsync("/Reports/FacultyDois?UserId=CF2S%40virginia.edu");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("cf2s", repository.LastUserId);
        Assert.Contains("1 unique DOI(s)", html);
        Assert.Contains("href=\"https://doi.org/10.1234/test\"", html);
        Assert.Contains("Faculty &lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public async Task RendersNoMatchAndInvalidUserIdAsDifferentStates()
    {
        var repository = new MemoryRepository();
        await using var host = await PageHost.StartAsync(repository);
        var noMatch = await host.Client.GetStringAsync("/Reports/FacultyDois?UserId=cf2s");
        var invalid = await host.Client.GetStringAsync("/Reports/FacultyDois?UserId=bad%27input");
        Assert.Contains("No faculty record matches", noMatch);
        Assert.Contains("Enter a valid userid", invalid);
        Assert.DoesNotContain("No faculty record matches", invalid);
        Assert.Equal(1, repository.Calls);
    }

    [Fact]
    public async Task RendersKnownFacultyWithNoDois()
    {
        await using var host = await PageHost.StartAsync(new MemoryRepository(
            new FacultyDoiSourceRow("Faculty One", null, null, null, "Stored")));
        var html = await host.Client.GetStringAsync("/Reports/FacultyDois?UserId=cf2s");
        Assert.Contains("This faculty member was found, but no valid DOIs", html);
        Assert.DoesNotContain("No faculty record matches", html);
    }

    [Fact]
    public async Task DeniesAnonymousAndNonReaderRequestsBeforeQuerying()
    {
        var repository = new MemoryRepository();
        await using var host = await PageHost.StartAsync(repository);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Role");
        var anonymous = await host.Client.GetAsync("/Reports/FacultyDois?UserId=cf2s");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        host.Client.DefaultRequestHeaders.Add("X-Test-Role", "Faculty");
        var forbidden = await host.Client.GetAsync("/Reports/FacultyDois?UserId=cf2s");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(0, repository.Calls);
    }

    private sealed class MemoryRepository : IFacultyDoiRepository
    {
        private readonly FacultyDoiSourceRow[] _rows;
        public MemoryRepository(params FacultyDoiSourceRow[] rows) => _rows = rows;
        public int Calls { get; private set; }
        public string? LastUserId { get; private set; }
        public Task<IReadOnlyList<FacultyDoiSourceRow>> GetByUserIdAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastUserId = userId;
            return Task.FromResult<IReadOnlyList<FacultyDoiSourceRow>>(_rows);
        }
    }

    private sealed class PageHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }

        private PageHost(WebApplication app, string address)
        {
            _app = app;
            Client = new HttpClient { BaseAddress = new Uri(address) };
            Client.DefaultRequestHeaders.Add("X-Test-Role", "Reader");
        }

        public static async Task<PageHost> StartAsync(IFacultyDoiRepository repository)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(FacultyDoisModel).Assembly.GetName().Name,
                EnvironmentName = "Development"
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddRazorPages().AddApplicationPart(typeof(FacultyDoisModel).Assembly);
            builder.Services.AddSingleton(repository);
            builder.Services.AddScoped<FacultyDoiLookupService>();
            builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
            builder.Services.AddAuthorization(options =>
                options.AddPolicy("ReaderPolicy", policy => policy.RequireRole("Reader", "Reviewer", "Admin")));

            var app = builder.Build();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapRazorPages();
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            return new PageHost(app, addresses.Addresses.Single());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (string.IsNullOrEmpty(role)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "Test reader"), new Claim(ClaimTypes.Role, role)
            }, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
