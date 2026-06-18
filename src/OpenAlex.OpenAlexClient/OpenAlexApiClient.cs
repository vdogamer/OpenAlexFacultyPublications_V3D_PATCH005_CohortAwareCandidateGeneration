using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OpenAlex.OpenAlexClient;

public sealed class OpenAlexApiClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenAlexOptions _options;

    public OpenAlexApiClient(HttpClient httpClient, IOptions<OpenAlexOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }
    }

    public Task<OpenAlexCursorPage> GetInstitutionsPageAsync(string filter, string cursor = "*", CancellationToken cancellationToken = default)
    {
        return GetCursorPageAsync("institutions", filter, cursor, cancellationToken);
    }

    public Task<OpenAlexCursorPage> GetAuthorsPageAsync(string filter, string cursor = "*", CancellationToken cancellationToken = default)
    {
        return GetCursorPageAsync("authors", filter, cursor, cancellationToken);
    }

    public Task<OpenAlexCursorPage> GetWorksPageAsync(string filter, string cursor = "*", CancellationToken cancellationToken = default)
    {
        return GetCursorPageAsync("works", filter, cursor, cancellationToken);
    }

    public string BuildInstitutionIdFilter(string fieldName = "id")
    {
        if (_options.InstitutionIds.Count == 0)
        {
            throw new InvalidOperationException("At least one OpenAlex institution ID is required.");
        }

        return $"{fieldName}:{string.Join('|', _options.InstitutionIds)}";
    }

    public string BuildCursorRequestUri(string endpoint, string filter, string cursor)
    {
        var query = new Dictionary<string, string?>
        {
            ["filter"] = filter,
            ["per-page"] = Math.Clamp(_options.PerPage, 1, 200).ToString(),
            ["cursor"] = cursor
        };

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            query["api_key"] = _options.ApiKey;
        }

        // Kept for backward compatibility/documentation. As of 2026, OpenAlex API keys replaced the old mailto polite-pool behavior.
        if (!string.IsNullOrWhiteSpace(_options.MailTo))
        {
            query["mailto"] = _options.MailTo;
        }

        return endpoint + "?" + string.Join("&", query
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .Select(kvp => Uri.EscapeDataString(kvp.Key) + "=" + Uri.EscapeDataString(kvp.Value!)));
    }

    private async Task<OpenAlexCursorPage> GetCursorPageAsync(string endpoint, string filter, string cursor, CancellationToken cancellationToken)
    {
        var uri = BuildCursorRequestUri(endpoint, filter, cursor);

        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContentLength = response.Content.Headers.ContentLength;

        var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenAlex returned an empty JSON document.");

        var root = document.RootElement;
        var meta = root.TryGetProperty("meta", out var metaElement) && metaElement.ValueKind == JsonValueKind.Object
            ? metaElement
            : default;

        var resultsCount = root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
            ? results.GetArrayLength()
            : 0;

        return new OpenAlexCursorPage
        {
            RequestUri = uri,
            HttpStatusCode = (int)response.StatusCode,
            ResponseContentLength = responseContentLength,
            NextCursor = GetString(meta, "next_cursor"),
            ResultsCount = resultsCount,
            MetaCount = GetInt(meta, "count"),
            MetaDbResponseTimeMs = GetInt(meta, "db_response_time_ms"),
            MetaPage = GetInt(meta, "page"),
            MetaPerPage = GetInt(meta, "per_page"),
            RawDocument = document
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
