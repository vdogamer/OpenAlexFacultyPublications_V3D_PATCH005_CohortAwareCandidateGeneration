using System.Text.Json;

namespace OpenAlex.OpenAlexClient;

public sealed class OpenAlexEntityResponse : IDisposable
{
    public string RequestUri { get; init; } = string.Empty;
    public int HttpStatusCode { get; init; }
    public bool IsSuccessStatusCode { get; init; }
    public long? ResponseContentLength { get; init; }
    public JsonDocument? RawDocument { get; init; }
    public string? ErrorContent { get; init; }

    public void Dispose()
    {
        RawDocument?.Dispose();
    }
}
