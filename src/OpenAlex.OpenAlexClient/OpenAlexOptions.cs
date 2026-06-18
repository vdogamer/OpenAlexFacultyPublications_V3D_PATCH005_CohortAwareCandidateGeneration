namespace OpenAlex.OpenAlexClient;

public sealed class OpenAlexOptions
{
    public string BaseUrl { get; set; } = "https://api.openalex.org";
    public string MailTo { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int PerPage { get; set; } = 100;
    public List<string> InstitutionIds { get; set; } = new();
}
