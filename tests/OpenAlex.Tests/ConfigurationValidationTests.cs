namespace OpenAlex.Tests;

public sealed class ConfigurationValidationTests
{
    [Fact]
    public void ValidateConfig_ReturnsErrors_WhenConnectionStringMissing()
    {
        var result = TestConfigurationValidator.Validate("", "https://api.openalex.org", "ctd8z@virginia.edu", ["I4210107925"]);
        Assert.Contains(result.Errors, error => error.Contains("connection string", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateConfig_ReturnsErrors_WhenInstitutionsMissing()
    {
        var result = TestConfigurationValidator.Validate("Server=.;Database=Test;", "https://api.openalex.org", "ctd8z@virginia.edu", []);
        Assert.Contains(result.Errors, error => error.Contains("institution", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateConfig_ReturnsNoErrors_ForBaselineConfig()
    {
        var result = TestConfigurationValidator.Validate("Server=.;Database=OpenAlexIngestion_Test;", "https://api.openalex.org", "ctd8z@virginia.edu", ["I4210107925"]);
        Assert.Empty(result.Errors);
    }
}

internal sealed record TestConfigValidationResult(IReadOnlyList<string> Errors);

internal static class TestConfigurationValidator
{
    public static TestConfigValidationResult Validate(string? connectionString, string? openAlexBaseUrl, string? mailTo, IReadOnlyList<string> institutionIds)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(connectionString)) errors.Add("Database connection string is required.");
        if (string.IsNullOrWhiteSpace(openAlexBaseUrl)) errors.Add("OpenAlex base URL is required.");
        if (string.IsNullOrWhiteSpace(mailTo)) errors.Add("OpenAlex mailto value is strongly recommended for polite API usage.");
        if (institutionIds.Count == 0) errors.Add("At least one OpenAlex institution ID is required.");
        return new TestConfigValidationResult(errors);
    }
}
