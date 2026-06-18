using Microsoft.Extensions.Options;
using OpenAlex.OpenAlexClient;
using OpenAlex.Web.Auth;

namespace OpenAlex.Web.Diagnostics;

public sealed class ConfigurationPreflightService
{
    private readonly IConfiguration _configuration;
    private readonly OpenAlexOptions _openAlexOptions;
    private readonly AuthOptions _authOptions;

    public ConfigurationPreflightService(IConfiguration configuration, IOptions<OpenAlexOptions> openAlexOptions, IOptions<AuthOptions> authOptions)
    {
        _configuration = configuration;
        _openAlexOptions = openAlexOptions.Value;
        _authOptions = authOptions.Value;
    }

    public ConfigurationValidationResult Validate()
    {
        var result = new ConfigurationValidationResult();
        var databaseConnectionString = _configuration["Database:ConnectionString"] ?? _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(databaseConnectionString)) result.Errors.Add("Database connection string is missing. Set Database:ConnectionString or ConnectionStrings:DefaultConnection.");
        if (string.IsNullOrWhiteSpace(_openAlexOptions.BaseUrl)) result.Errors.Add("OpenAlex:BaseUrl is missing.");
        if (_openAlexOptions.InstitutionIds.Count == 0) result.Errors.Add("OpenAlex:InstitutionIds must contain at least one institution ID.");
        if (string.IsNullOrWhiteSpace(_openAlexOptions.MailTo) || _openAlexOptions.MailTo.Contains("your-email", StringComparison.OrdinalIgnoreCase)) result.Warnings.Add("OpenAlex:MailTo is not set to a real team/person email.");
        if (!string.Equals(_authOptions.Mode, "DemoCookie", StringComparison.OrdinalIgnoreCase) && !string.Equals(_authOptions.Mode, "ShibbolethHeaders", StringComparison.OrdinalIgnoreCase)) result.Errors.Add("Auth:Mode must be DemoCookie or ShibbolethHeaders.");
        if (string.Equals(_authOptions.Mode, "ShibbolethHeaders", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(_authOptions.ShibbolethHeaders.UserNameHeader)) result.Errors.Add("Auth:ShibbolethHeaders:UserNameHeader is required in ShibbolethHeaders mode.");
            result.Warnings.Add("Confirm IIS/Shibboleth strips spoofed browser-supplied headers before production use.");
        }
        return result;
    }
}
