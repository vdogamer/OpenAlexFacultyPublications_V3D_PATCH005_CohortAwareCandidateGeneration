using Microsoft.AspNetCore.Authentication.Cookies;
using OpenAlex.Data;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;
using OpenAlex.Matching;
using OpenAlex.Reporting;
using OpenAlex.Web.Auth;
using OpenAlex.Web.Diagnostics;
using OpenAlex.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// PATCH017B8A security hardening: suppress informational HttpClient request URI
// logging for the OpenAlex client because the API credential is query-string based.
builder.Logging.AddFilter("System.Net.Http.HttpClient.OpenAlexApiClient.LogicalHandler", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.OpenAlexApiClient.ClientHandler", LogLevel.Warning);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Review", "ReviewerPolicy");
    options.Conventions.AuthorizeFolder("/Reports", "ReaderPolicy");
    options.Conventions.AuthorizePage("/Operations", "AdminPolicy");
    options.Conventions.AuthorizeFolder("/Operations", "AdminPolicy");
    options.Conventions.AuthorizeFolder("/Admin", "AdminPolicy");
});

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<FacultyProfileHandoffOptions>(builder.Configuration.GetSection("FacultyProfileHandoff"));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "OpenAlex.V3A.Auth";
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ReaderPolicy", policy => policy.RequireRole("Reader", "Reviewer", "Admin"));
    options.AddPolicy("ReviewerPolicy", policy => policy.RequireRole("Reviewer", "Admin"));
    options.AddPolicy("AdminPolicy", policy => policy.RequireRole("Admin"));
});

builder.Services.AddOpenAlexData(builder.Configuration);
// PATCH003 safeguard: ensure manual faculty intake repository is registered even if older data extensions are deployed.
builder.Services.AddScoped<FacultyRepository>(); // PATCH003
builder.Services.AddOpenAlexClient(builder.Configuration);
builder.Services.AddOpenAlexOperationalLogging(builder.Configuration);
builder.Services.AddScoped<FacultyAuthorScoringService>();
builder.Services.AddScoped<CsvExportService>();
builder.Services.AddScoped<ConfigurationPreflightService>();
builder.Services.AddScoped<FacultyAcademicOnlineMappingRepository>();
builder.Services.AddScoped<AcademicOnlineFacultyPublicationsService>();
builder.Services.AddScoped<AcademicOnlinePublicationHtmlFormatter>();
builder.Services.AddScoped<AcademicOnlineUniversalFacultyLookupService>();
builder.Services.AddScoped<SharedPublicationsService>();
builder.Services.AddScoped<FacultyProfileHandoffService>();
builder.Services.AddScoped<PublicationKeywordFilterService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<ShibbolethHeaderAuthenticationMiddleware>();
app.UseAuthorization();

app.MapRazorPages();
app.Run();
