namespace OpenAlex.OpenAlexClient;

public sealed class AcademicOnlineOptions
{
    public string BaseUrl { get; set; } = "https://profile.api.academic.online/virginia";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
}
