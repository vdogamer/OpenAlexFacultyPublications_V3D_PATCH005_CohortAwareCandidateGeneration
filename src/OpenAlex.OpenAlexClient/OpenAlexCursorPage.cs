using System.Text.Json;

namespace OpenAlex.OpenAlexClient;

public sealed class OpenAlexCursorPage : IDisposable
{
    public string RequestUri { get; set; } = string.Empty;
    public int HttpStatusCode { get; set; }
    public long? ResponseContentLength { get; set; }

    public string? NextCursor { get; set; }
    public int ResultsCount { get; set; }

    public int? MetaCount { get; set; }
    public int? MetaDbResponseTimeMs { get; set; }
    public int? MetaPage { get; set; }
    public int? MetaPerPage { get; set; }

    public JsonDocument RawDocument { get; set; } = null!;

    public JsonElement ResultsElement => RawDocument.RootElement.GetProperty("results");

    public void Dispose()
    {
        RawDocument?.Dispose();
    }
}
