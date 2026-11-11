using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.Web.Pages.Reports;
using OpenAlex.Web.Services.PubMed;

namespace OpenAlex.Tests;

public sealed class PubMedCandidatePageHttpTests
{
    [Fact]
    public async Task InitialPageAndLoadingFacultyDoNotCallPubMed()
    {
        var faculty = Faculty();
        var provider = new PubMedCandidateLookupTests.ProviderStub();
        await using var host = await PageHost.StartAsync(faculty,provider);
        var initial = await host.Client.GetStringAsync("/Reports/PubMedCandidates");
        Assert.Contains("PubMed Candidate Lookup",initial);
        Assert.Equal(0,faculty.Calls);
        var loaded = await host.Client.GetStringAsync("/Reports/PubMedCandidates?UserId=DEMO01");
        Assert.Contains("Faculty record: demo01",loaded);
        Assert.Contains("Example A",loaded);
        Assert.Contains("Search PubMed",loaded);
        Assert.DoesNotContain("No PubMed citations were returned",loaded);
        Assert.Equal(1,faculty.Calls);
        Assert.Equal(0,provider.Calls);
    }

    [Fact]
    public async Task CandidatePageRetainsPmidOnlyResultsAndEncodesReturnedNamesAndTitles()
    {
        var faculty = Faculty("Alex <script>Example</script>");
        var provider = new PubMedCandidateLookupTests.ProviderStub
            { Articles=PubMedXmlParser.Parse(PubMedClientTests.FixtureXml) };
        await using var host = await PageHost.StartAsync(faculty,provider);
        var html = await host.Client.GetStringAsync("/Reports/PubMedCandidates?UserId=demo01&Search=true&Mode=Identifiers&Identifiers=101%20PMC201");
        Assert.Equal(1,provider.Calls);
        Assert.Contains("2 candidate citation(s) retrieved",html);
        Assert.Contains("authorship not confirmed",html);
        Assert.Contains("DOI not supplied",html);
        Assert.Contains("https://pubmed.ncbi.nlm.nih.gov/102/",html);
        Assert.Contains("https://pmc.ncbi.nlm.nih.gov/articles/PMC201/",html);
        Assert.Contains("Alex &lt;script&gt;",html);
        Assert.Contains("Title &lt;script&gt;",html);
        Assert.Contains("Previous University",html);
        Assert.Contains("Investigator/collaborator",html);
        Assert.DoesNotContain("<script>",html);
    }

    [Fact]
    public async Task UnknownFacultyAndEmptyPubMedResultAreDifferentStates()
    {
        var provider = new PubMedCandidateLookupTests.ProviderStub();
        await using var unknownHost = await PageHost.StartAsync(new PubMedCandidateLookupTests.FacultyStub(),provider);
        var unknown = await unknownHost.Client.GetStringAsync("/Reports/PubMedCandidates?UserId=unknown99&Search=true&Mode=Author&AuthorTerms=Example%20A");
        Assert.Contains("No faculty directory record matches",unknown);
        Assert.DoesNotContain("No PubMed citations were returned",unknown);
        Assert.Equal(0,provider.Calls);
        await using var knownHost = await PageHost.StartAsync(Faculty(),provider);
        var empty = await knownHost.Client.GetStringAsync("/Reports/PubMedCandidates?UserId=demo01&Search=true&Mode=Author&AuthorTerms=Example%20A");
        Assert.Contains("No PubMed citations were returned",empty);
        Assert.Contains("does not establish that the faculty member has never published",empty);
        Assert.DoesNotContain("No faculty directory record matches",empty);
        Assert.Equal(1,provider.Calls);
    }

    [Fact]
    public async Task FailuresPreserveTheQueryLinkWithoutClaimingZeroPublications()
    {
        var provider = new PubMedCandidateLookupTests.ProviderStub
            { Error=new PubMedProviderException("PubMed is temporarily unavailable.") };
        await using var host = await PageHost.StartAsync(Faculty(),provider);
        var html = await host.Client.GetStringAsync("/Reports/PubMedCandidates?UserId=demo01&Search=true&Mode=Author&AuthorTerms=Example%20A");
        Assert.Contains("Lookup could not be completed",html);
        Assert.Contains("PubMed is temporarily unavailable",html);
        Assert.Contains("Open the same search in PubMed",html);
        Assert.DoesNotContain("No PubMed citations were returned",html);
    }

    [Theory]
    [InlineData("UserId=bad%20input!&Search=true")]
    [InlineData("UserId=demo01&Search=true&Mode=Author&AuthorTerms=Example%20A%5Bau%5D")]
    [InlineData("UserId=demo01&Search=true&Mode=Orcid&Orcid=0000-0002-1825-0098")]
    [InlineData("UserId=demo01&Search=true&Mode=Author&AuthorTerms=Example%20A&Limit=1000")]
    [InlineData("UserId=demo01&Search=true&YearFrom=not-a-year")]
    public async Task InvalidInputsDoNotCallPubMed(string query)
    {
        var provider = new PubMedCandidateLookupTests.ProviderStub();
        await using var host = await PageHost.StartAsync(Faculty(),provider);
        var html = await host.Client.GetStringAsync("/Reports/PubMedCandidates?"+query);
        Assert.Contains("Lookup could not be completed",html);
        Assert.Equal(0,provider.Calls);
    }

    [Fact]
    public async Task ReaderAuthorizationIsEnforcedBeforeAnyLookup()
    {
        var faculty=Faculty();
        var provider=new PubMedCandidateLookupTests.ProviderStub();
        await using var host=await PageHost.StartAsync(faculty,provider);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Role");
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Client.GetAsync("/Reports/PubMedCandidates?UserId=demo01&Search=true")).StatusCode);
        host.Client.DefaultRequestHeaders.Add("X-Test-Role","Faculty");
        Assert.Equal(HttpStatusCode.Forbidden,(await host.Client.GetAsync("/Reports/PubMedCandidates?UserId=demo01&Search=true")).StatusCode);
        Assert.Equal(0,faculty.Calls);
        Assert.Equal(0,provider.Calls);
    }

    private static PubMedCandidateLookupTests.FacultyStub Faculty(string name="Alex Example") =>
        new(new FacultyListItem { ComputingId="demo01",DisplayName=name,IsRosterMember=true,DepartmentName="Fixture department" });

    private sealed class PageHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        private PageHost(WebApplication app,string address)
        {
            _app=app; Client=new HttpClient { BaseAddress=new Uri(address) };
            Client.DefaultRequestHeaders.Add("X-Test-Role","Reader");
        }
        public static async Task<PageHost> StartAsync(IFacultyIdentityLookup faculty,IPubMedClient provider)
        {
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions
                { ApplicationName=typeof(PubMedCandidatesModel).Assembly.GetName().Name,EnvironmentName="Development" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddRazorPages().AddApplicationPart(typeof(PubMedCandidatesModel).Assembly);
            builder.Services.AddSingleton(faculty);
            builder.Services.AddSingleton(provider);
            builder.Services.AddScoped<PubMedCandidateLookupService>();
            builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,TestAuthenticationHandler>("Test",_=>{});
            builder.Services.AddAuthorization(options=>options.AddPolicy("ReaderPolicy",policy=>policy.RequireRole("Reader","Reviewer","Admin")));
            var app=builder.Build();
            app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapRazorPages();
            await app.StartAsync();
            var addresses=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            return new PageHost(app,addresses.Addresses.Single());
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await _app.DisposeAsync(); }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder)
            : base(options,logger,encoder) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role=Request.Headers["X-Test-Role"].ToString();
            if (role.Length==0) return Task.FromResult(AuthenticateResult.NoResult());
            var principal=new ClaimsPrincipal(new ClaimsIdentity(new[]
                {new Claim(ClaimTypes.Name,"Test reader"),new Claim(ClaimTypes.Role,role)},Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal,Scheme.Name)));
        }
    }
}
