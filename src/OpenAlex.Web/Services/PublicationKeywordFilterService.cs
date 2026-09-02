using System.Text.Json;
using OpenAlex.OpenAlexClient;

namespace OpenAlex.Web.Services;

public sealed class PublicationKeywordFilterService
{
    public PublicationKeywordFilterResult Apply(
        IReadOnlyList<AcademicOnlinePublicationItem> publications,
        string? keywordQuery,
        string? matchMode)
    {
        var terms = ParseTerms(keywordQuery);
        var normalizedMode = NormalizeMatchMode(matchMode);

        if (terms.Count == 0)
        {
            return new PublicationKeywordFilterResult
            {
                Publications = publications,
                SourcePublicationCount = publications.Count,
                KeywordQuery = null,
                MatchMode = normalizedMode,
                Terms = terms
            };
        }

        var matches = publications
            .Where(publication => Matches(publication, terms, normalizedMode))
            .ToList();

        return new PublicationKeywordFilterResult
        {
            Publications = matches,
            SourcePublicationCount = publications.Count,
            KeywordQuery = string.Join(", ", terms),
            MatchMode = normalizedMode,
            Terms = terms
        };
    }

    public static string NormalizeMatchMode(string? value)
    {
        return string.Equals(value?.Trim(), "all", StringComparison.OrdinalIgnoreCase)
            ? "all"
            : "any";
    }

    private static IReadOnlyList<string> ParseTerms(string? keywordQuery)
    {
        if (string.IsNullOrWhiteSpace(keywordQuery))
        {
            return Array.Empty<string>();
        }

        return keywordQuery
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.Trim())
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
    }

    private static bool Matches(
        AcademicOnlinePublicationItem publication,
        IReadOnlyList<string> terms,
        string matchMode)
    {
        var searchableText = BuildSearchableText(publication);

        return matchMode == "all"
            ? terms.All(term => Contains(searchableText, term))
            : terms.Any(term => Contains(searchableText, term));
    }

    private static bool Contains(string searchableText, string term)
    {
        return searchableText.Contains(
            term,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSearchableText(
        AcademicOnlinePublicationItem publication)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(publication.Title))
        {
            parts.Add(publication.Title);
        }

        if (!string.IsNullOrWhiteSpace(publication.Venue))
        {
            parts.Add(publication.Venue);
        }

        parts.AddRange(ExtractKeywordText(publication.KeywordsJson));

        return string.Join("\n", parts);
    }

    private static IReadOnlyList<string> ExtractKeywordText(string? keywordsJson)
    {
        if (string.IsNullOrWhiteSpace(keywordsJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(keywordsJson);

            return EnumerateStringValues(document.RootElement)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();
        }
        catch (JsonException)
        {
            // Preserve filtering availability even if an upstream keyword payload
            // is malformed. Title and venue matching still work.
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> EnumerateStringValues(
        JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var text in EnumerateStringValues(item))
                    {
                        yield return text;
                    }
                }
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var text in EnumerateStringValues(property.Value))
                    {
                        yield return text;
                    }
                }
                break;
        }
    }
}

public sealed class PublicationKeywordFilterResult
{
    public IReadOnlyList<AcademicOnlinePublicationItem> Publications { get; init; } =
        Array.Empty<AcademicOnlinePublicationItem>();

    public int SourcePublicationCount { get; init; }
    public string? KeywordQuery { get; init; }
    public string MatchMode { get; init; } = "any";
    public IReadOnlyList<string> Terms { get; init; } = Array.Empty<string>();
}
