using System.Net;
using System.Net.Http.Headers;
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
using OpenAlex.Web.Pages.Faculty;
using OpenAlex.Web.Services;

namespace OpenAlex.Tests;

public sealed class FacultyPhotoTests
{
    [Fact]
    public async Task RosterPhotoUsesExactNormalizedComputingIdAndStoredDirectoryPersonId()
    {
        var faculty = new FacultyStub(new FacultyListItem() { ComputingId="demo01",IsRosterMember=true,DirectoryPersonId=123 });
        var handler = new Handler(HttpStatusCode.OK,"image/jpeg");
        using var http = new HttpClient(handler);
        var result = await Service(faculty,http).LookupAsync(" DEMO01@Virginia.edu ");
        Assert.Equal("demo01",faculty.LastId);
        Assert.Equal(FacultyPhotoStatus.Found,result.Status);
        Assert.Equal("https://apps.med.virginia.edu/ResearchFacultyDirectory/Exchange/Photos/Small/demo01_123.jpg",result.PhotoUrl);
        Assert.Equal(HttpMethod.Head,Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task CheckedOfficialProfileLinksSupportFacultyOutsideTheMedicineRoster()
    {
        var faculty = new FacultyStub(new FacultyListItem() { ComputingId="demo01",InRebuildImport=true });
        var options = Links();
        var handler = new Handler(HttpStatusCode.OK,"image/jpeg");
        using var http = new HttpClient(handler);
        var result = await Service(faculty,http,options).LookupAsync("demo01");
        Assert.Equal(FacultyPhotoStatus.Found,result.Status);
        Assert.Equal(options.VerifiedLinks["demo01"].PhotoUrl,result.PhotoUrl);
        Assert.Equal(options.VerifiedLinks["demo01"].ProfileUrl,result.ProfileUrl);
    }

    [Theory]
    [InlineData("http://department.virginia.edu/photo.jpg")]
    [InlineData("https://virginia.edu.evil.example/photo.jpg")]
    [InlineData("https://department.virginia.edu@evil.example/photo.jpg")]
    [InlineData("https://department.virginia.edu:444/photo.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/photo.jpg")]
    public async Task UntrustedPhotoOverridesAreNotRequestedOrReturned(string photoUrl)
    {
        var options = Links(); options.VerifiedLinks["demo01"].PhotoUrl=photoUrl;
        var handler = new Handler(HttpStatusCode.OK,"image/jpeg");
        using var http = new HttpClient(handler);
        var result = await Service(new FacultyStub(new FacultyListItem() { ComputingId="demo01" }),http,options).LookupAsync("demo01");
        Assert.Equal(FacultyPhotoStatus.NotAvailable,result.Status);
        Assert.Null(result.PhotoUrl);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnknownFacultyMissingDirectoryIdsAndConflictingIdentitiesMakeNoWebRequest()
    {
        var handler = new Handler(HttpStatusCode.OK,"image/jpeg");
        using var http = new HttpClient(handler);
        Assert.Equal(FacultyPhotoStatus.FacultyNotFound,(await Service(new FacultyStub(),http,Links()).LookupAsync("demo01")).Status);
        Assert.Equal(FacultyPhotoStatus.NotAvailable,(await Service(new FacultyStub(new FacultyListItem() { ComputingId="demo01" }),http).LookupAsync("demo01")).Status);
        // A directory ID cannot be borrowed from a native record or guessed from a name/source key.
        Assert.Equal(FacultyPhotoStatus.NotAvailable,(await Service(new FacultyStub(new FacultyListItem() { ComputingId="demo01",DirectoryPersonId=123 }),http).LookupAsync("demo01")).Status);
        var conflict = new FacultyStub(new FacultyListItem() { FacultyId=1 },new FacultyListItem() { FacultyId=2 });
        Assert.Equal(FacultyPhotoStatus.IdentityConflict,(await Service(conflict,http,Links()).LookupAsync("demo01")).Status);
        await Assert.ThrowsAsync<ArgumentException>(() => Service(new FacultyStub(),http).LookupAsync("bad input!"));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(404,"text/html",FacultyPhotoStatus.NotAvailable)]
    [InlineData(410,"text/html",FacultyPhotoStatus.NotAvailable)]
    [InlineData(200,"text/html",FacultyPhotoStatus.NotAvailable)]
    [InlineData(403,"text/html",FacultyPhotoStatus.ProviderUnavailable)]
    [InlineData(429,"text/html",FacultyPhotoStatus.ProviderUnavailable)]
    [InlineData(503,"text/html",FacultyPhotoStatus.ProviderUnavailable)]
    [InlineData(302,"text/html",FacultyPhotoStatus.ProviderUnavailable)]
    public async Task MissingPhotosAndProviderFailuresAreDifferentStates(int code,string contentType,FacultyPhotoStatus status)
    {
        var handler = new Handler((HttpStatusCode)code,contentType);
        using var http = new HttpClient(handler);
        var result = await Service(new FacultyStub(new FacultyListItem() { ComputingId="demo01" }),http,Links()).LookupAsync("demo01");
        Assert.Equal(status,result.Status);
        Assert.Null(result.PhotoUrl);
        Assert.NotNull(result.ProfileUrl);
    }

    [Fact]
    public async Task HeadUnsupportedFallsBackToGetHeadersWithoutDownloadingThePhoto()
    {
        var handler = new Handler(HttpStatusCode.OK,"image/png") { HeadUnsupported=true };
        using var http = new HttpClient(handler);
        var result = await Service(new FacultyStub(new FacultyListItem() { ComputingId="demo01" }),http,Links()).LookupAsync("demo01");
        Assert.Equal(FacultyPhotoStatus.Found,result.Status);
        Assert.Equal(new[] { HttpMethod.Head,HttpMethod.Get },handler.Requests.Select(r=>r.Method));
    }

    [Fact]
    public async Task RequestCancellationIsPropagated()
    {
        var handler = new Handler(HttpStatusCode.OK,"image/jpeg");
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(new FacultyStub(new FacultyListItem() { ComputingId="demo01" }),http,Links()).LookupAsync("demo01",cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PhotoPageRequiresReaderAccessAndRedirectsOnlyAfterAnImageResponse()
    {
        var faculty = new FacultyStub(new FacultyListItem() { ComputingId="demo01",DisplayName="Synthetic Faculty" });
        var handler = new Handler(HttpStatusCode.OK,"image/jpeg");
        using var provider = new HttpClient(handler);
        await using var host = await PageHost.StartAsync(Service(faculty,provider,Links()));
        var anonymous = await host.Client.GetAsync("/Faculty/Photo?UserId=demo01");
        Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        Assert.Equal(0,faculty.Calls);
        host.Client.DefaultRequestHeaders.Add("X-Test-Role","Faculty");
        Assert.Equal(HttpStatusCode.Forbidden,(await host.Client.GetAsync("/Faculty/Photo?UserId=demo01")).StatusCode);
        Assert.Equal(0,faculty.Calls);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Role");
        host.Client.DefaultRequestHeaders.Add("X-Test-Role","Reader");
        var found = await host.Client.GetAsync("/Faculty/Photo?UserId=demo01");
        Assert.Equal(HttpStatusCode.Redirect,found.StatusCode);
        Assert.Equal(Links().VerifiedLinks["demo01"].PhotoUrl,found.Headers.Location!.AbsoluteUri);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PhotoPageRendersMissingAndConnectionFailureMessagesWithOfficialProfileFallback()
    {
        var faculty = new FacultyStub(new FacultyListItem() { ComputingId="demo01" });
        var handler = new Handler(HttpStatusCode.NotFound,"text/html");
        using var provider = new HttpClient(handler);
        await using var host = await PageHost.StartAsync(Service(faculty,provider,Links()));
        host.Client.DefaultRequestHeaders.Add("X-Test-Role","Reader");
        var missing = await host.Client.GetStringAsync("/Faculty/Photo?UserId=demo01");
        Assert.Contains("No photo link could be confirmed",missing);
        Assert.Contains("https://department.virginia.edu/faculty/demo01",missing);
        handler.Status=HttpStatusCode.ServiceUnavailable;
        var failed = await host.Client.GetStringAsync("/Faculty/Photo?UserId=demo01");
        Assert.Contains("could not be reached or checked",failed);
        Assert.DoesNotContain("No photo link could be confirmed",failed);
        var invalid = await host.Client.GetStringAsync("/Faculty/Photo?UserId=bad%20input!");
        Assert.Contains("Enter a valid UVA ComputingID",invalid);
        Assert.Equal(2,handler.Requests.Count);
    }

    private static FacultyPhotoOptions Links() => new()
    {
        VerifiedLinks = new(StringComparer.OrdinalIgnoreCase)
        {
            ["demo01"] = new() { PhotoUrl="https://department.virginia.edu/photos/demo01.jpg",ProfileUrl="https://department.virginia.edu/faculty/demo01" }
        }
    };
    private static FacultyPhotoService Service(FacultyStub faculty,HttpClient http,FacultyPhotoOptions? options=null) =>
        new(faculty,http,Options.Create(options??new FacultyPhotoOptions()));

    private sealed class FacultyStub : IFacultyIdentityLookup
    {
        private readonly FacultyListItem[] _rows;
        public FacultyStub(params FacultyListItem[] rows) => _rows=rows;
        public string? LastId { get; private set; }
        public int Calls { get; private set; }
        public Task<IReadOnlyList<FacultyListItem>> GetByComputingIdAsync(string id,CancellationToken token=default)
        { token.ThrowIfCancellationRequested(); LastId=id; Calls++; return Task.FromResult<IReadOnlyList<FacultyListItem>>(_rows); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; }
        public string ContentType { get; }
        public bool HeadUnsupported { get; init; }
        public List<(HttpMethod Method,Uri Url)> Requests { get; } = new();
        public Handler(HttpStatusCode status,string contentType) { Status=status;ContentType=contentType; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests.Add((request.Method,request.RequestUri!));
            var response = new HttpResponseMessage(HeadUnsupported && request.Method==HttpMethod.Head ? HttpStatusCode.MethodNotAllowed : Status)
                { Content=new ByteArrayContent(Array.Empty<byte>()) };
            response.Content.Headers.ContentType=new MediaTypeHeaderValue(ContentType);
            return Task.FromResult(response);
        }
    }
    private sealed class PageHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        private PageHost(WebApplication app,string address)
        { _app=app;Client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { BaseAddress=new Uri(address) }; }
        public static async Task<PageHost> StartAsync(FacultyPhotoService service)
        {
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions
                { ApplicationName=typeof(PhotoModel).Assembly.GetName().Name,EnvironmentName="Development" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddRazorPages().AddApplicationPart(typeof(PhotoModel).Assembly);
            builder.Services.AddSingleton(service);
            builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,TestAuth>("Test",_=>{});
            builder.Services.AddAuthorization(options=>options.AddPolicy("ReaderPolicy",p=>p.RequireRole("Reader","Reviewer","Admin")));
            var app=builder.Build();app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.MapRazorPages();
            await app.StartAsync();
            return new(app,app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        }
        public async ValueTask DisposeAsync() { Client.Dispose();await _app.DisposeAsync(); }
    }
    private sealed class TestAuth : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder) : base(options,logger,encoder) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role=Request.Headers["X-Test-Role"].ToString();
            if(role.Length==0)return Task.FromResult(AuthenticateResult.NoResult());
            var identity=new ClaimsIdentity(new[] {new Claim(ClaimTypes.Name,"Synthetic reader"),new Claim(ClaimTypes.Role,role)},Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity),Scheme.Name)));
        }
    }
}
