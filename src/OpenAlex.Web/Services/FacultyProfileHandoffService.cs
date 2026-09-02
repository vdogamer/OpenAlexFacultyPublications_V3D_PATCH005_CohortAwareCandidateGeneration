using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace OpenAlex.Web.Services;

public sealed class FacultyProfileHandoffService
{
    private static readonly Regex ComputingIdPattern =
        new("^[A-Za-z0-9]{2,32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly FacultyProfileHandoffOptions _options;

    public FacultyProfileHandoffService(
        IOptions<FacultyProfileHandoffOptions> options)
    {
        _options = options.Value;
    }

    public bool DirectPublishingEnabled => _options.DirectPublishingEnabled;

    public string? BuildPublicationsEditorUrl(
        string? computingId,
        string? email,
        string? userName)
    {
        if (!_options.Enabled ||
            string.IsNullOrWhiteSpace(_options.PublicationsEditBaseUrl))
        {
            return null;
        }

        var resolvedComputingId =
            NormalizeComputingId(computingId)
            ?? ExtractComputingIdFromVirginiaEmail(email)
            ?? ExtractComputingIdFromVirginiaEmail(userName);

        if (resolvedComputingId is null)
        {
            return null;
        }

        if (!Uri.TryCreate(
                _options.PublicationsEditBaseUrl.Trim(),
                UriKind.Absolute,
                out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps &&
             baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "FacultyProfileHandoff:PublicationsEditBaseUrl must be an absolute HTTP/HTTPS URL.");
        }

        var separator = string.IsNullOrWhiteSpace(baseUri.Query)
            ? "?"
            : "&";

        return baseUri + separator +
               "UserName=" + Uri.EscapeDataString(resolvedComputingId);
    }

    private static string? NormalizeComputingId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        return ComputingIdPattern.IsMatch(trimmed)
            ? trimmed
            : null;
    }

    private static string? ExtractComputingIdFromVirginiaEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        if (!trimmed.EndsWith(
                "@virginia.edu",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var at = trimmed.IndexOf('@');
        if (at <= 0)
        {
            return null;
        }

        return NormalizeComputingId(trimmed[..at]);
    }
}
