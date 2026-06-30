using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OpenAlex.OpenAlexClient;

public sealed class AcademicOnlineApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AcademicOnlineOptions _options;

    public AcademicOnlineApiClient(HttpClient httpClient, IOptions<AcademicOnlineOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }

        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(10, _options.TimeoutSeconds));
    }

    public Task<JsonDocument> SearchScholarsAsync(
        string facultyDisplayName,
        string? departmentName,
        int page,
        int rowsPerPage,
        CancellationToken cancellationToken = default)
    {
        var query = string.IsNullOrWhiteSpace(facultyDisplayName)
            ? Array.Empty<string>()
            : new[] { facultyDisplayName.Trim() };

        var departments = string.IsNullOrWhiteSpace(departmentName)
            ? Array.Empty<string>()
            : new[] { departmentName.Trim() };

        var body = new
        {
            full_text_search_query = facultyDisplayName?.Trim() ?? string.Empty,
            query,
            departments,
            page,
            rowsPerPage,
            advanced_search_input = new { }
        };

        return PostJsonDocumentAsync("scholars/filter_scholars_with_fts", body, cancellationToken);
    }

    public Task<JsonDocument> GetAuthorGeneralAsync(string authorId, CancellationToken cancellationToken = default)
    {
        return GetJsonDocumentAsync("author/general?authorId=" + Uri.EscapeDataString(authorId), cancellationToken);
    }

    public Task<JsonDocument> GetAuthorPublicationsAsync(
        string authorId,
        bool verifiedOnly,
        int page,
        int rowsPerPage,
        int? fromYear = null,
        int? toYear = null,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["authorId"] = authorId,
            ["verify"] = verifiedOnly ? "true" : "false",
            ["page"] = page.ToString(),
            ["rowsPerPage"] = rowsPerPage.ToString(),
            ["order_by"] = "publication_year",
            ["order"] = "desc"
        };

        if (fromYear.HasValue)
        {
            query["fromYear"] = fromYear.Value.ToString();
        }

        if (toYear.HasValue)
        {
            query["toYear"] = toYear.Value.ToString();
        }

        return GetJsonDocumentAsync("author/publications?" + BuildQueryString(query), cancellationToken);
    }

    private async Task<JsonDocument> GetJsonDocumentAsync(string relativeUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        AddAuthorization(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Academic Online returned an empty JSON document.");
    }

    private async Task<JsonDocument> PostJsonDocumentAsync(string relativeUri, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUri)
        {
            Content = JsonContent.Create(body)
        };
        AddAuthorization(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Academic Online returned an empty JSON document.");
    }

    private void AddAuthorization(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("AcademicOnline:ApiKey is missing. Store it in user-secrets or an environment variable; do not commit it to appsettings.json.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey.Trim());
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, string?> values)
    {
        return string.Join("&", values
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .Select(kvp => Uri.EscapeDataString(kvp.Key) + "=" + Uri.EscapeDataString(kvp.Value!)));
    }
}
