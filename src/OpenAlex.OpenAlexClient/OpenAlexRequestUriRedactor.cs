namespace OpenAlex.OpenAlexClient;

public static class OpenAlexRequestUriRedactor
{
    private static readonly string[] SensitiveQueryParameters =
    {
        "api_key",
        "mailto"
    };

    public static string Redact(string? requestUri)
    {
        if (string.IsNullOrWhiteSpace(requestUri))
        {
            return string.Empty;
        }

        var redacted = requestUri;
        foreach (var parameterName in SensitiveQueryParameters)
        {
            redacted = RedactQueryParameter(redacted, parameterName);
        }

        return redacted;
    }

    private static string RedactQueryParameter(string requestUri, string parameterName)
    {
        var token = parameterName + "=";
        var searchStart = 0;

        while (searchStart < requestUri.Length)
        {
            var index = requestUri.IndexOf(token, searchStart, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                break;
            }

            var valueStart = index + token.Length;
            var nextAmpersand = requestUri.IndexOf('&', valueStart);
            var valueEnd = nextAmpersand < 0 ? requestUri.Length : nextAmpersand;

            requestUri = requestUri[..valueStart] + "[REDACTED]" + requestUri[valueEnd..];
            searchStart = valueStart + "[REDACTED]".Length;
        }

        return requestUri;
    }
}
