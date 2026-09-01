namespace OpenAlex.OpenAlexClient;

public sealed class AcademicOnlineOptions
{
    public string BaseUrl { get; set; } = "https://profile.api.academic.online/virginia";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 60;

    // PATCH018 production pagination safeguards.
    public int PublicationRowsPerPage { get; set; } = 100;
    public int MaxPublicationPages { get; set; } = 100;
}
