using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OpenAlex.OpenAlexClient;

namespace OpenAlex.Web.Services;

public sealed class AcademicOnlinePublicationHtmlFormatter
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Default;

    public string BuildFragment(AcademicOnlineFacultyPublicationsResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<section class=\"faculty-publications\" data-source=\"academic-online\">");
        sb.AppendLine("  <header class=\"faculty-publications__header\">");
        sb.Append("    <h2>").Append(Encode(result.Profile.Name)).AppendLine(" — Publications</h2>");

        var profileMeta = BuildProfileMeta(result.Profile);
        if (!string.IsNullOrWhiteSpace(profileMeta))
        {
            sb.Append("    <p class=\"faculty-publications__meta\">")
                .Append(profileMeta)
                .AppendLine("</p>");
        }

        sb.Append("    <p class=\"faculty-publications__source\">Source: Academic Online")
            .Append(result.VerifiedOnly ? " · verified publications only" : " · all returned publications")
            .AppendLine("</p>");
        sb.AppendLine("  </header>");

        if (result.Publications.Count == 0)
        {
            sb.AppendLine("  <p>No publications were returned for the selected filters.</p>");
        }
        else
        {
            foreach (var yearGroup in result.Publications
                         .GroupBy(item => item.PublicationYear)
                         .OrderByDescending(group => group.Key ?? int.MinValue))
            {
                var yearLabel = yearGroup.Key?.ToString() ?? "Year not supplied";
                var yearId = yearGroup.Key?.ToString() ?? "unknown";
                sb.Append("  <section class=\"faculty-publications__year\" aria-labelledby=\"pub-year-")
                    .Append(EncodeAttribute(yearId))
                    .AppendLine("\">");
                sb.Append("    <h3 id=\"pub-year-")
                    .Append(EncodeAttribute(yearId))
                    .Append("\">")
                    .Append(Encode(yearLabel))
                    .AppendLine("</h3>");
                sb.AppendLine("    <ol class=\"faculty-publications__list\">");

                foreach (var publication in yearGroup
                             .OrderBy(item => item.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    AppendPublication(sb, publication);
                }

                sb.AppendLine("    </ol>");
                sb.AppendLine("  </section>");
            }
        }

        sb.AppendLine("</section>");
        return sb.ToString();
    }

    public string BuildDocument(AcademicOnlineFacultyPublicationsResult result)
    {
        var title = Encode(result.Profile.Name + " — Publications");
        var fragment = BuildFragment(result);

        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{{title}}</title>
  <style>
    body { font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; margin: 0; background: #f7f8fa; color: #1b1f23; }
    main { max-width: 1000px; margin: 0 auto; padding: 2rem 1.25rem 4rem; }
    .faculty-publications { background: white; border: 1px solid #d8dee4; border-radius: 12px; padding: 1.5rem; }
    .faculty-publications__header { border-bottom: 1px solid #d8dee4; margin-bottom: 1.5rem; padding-bottom: 1rem; }
    .faculty-publications__header h2 { margin-top: 0; }
    .faculty-publications__meta, .faculty-publications__source { color: #57606a; }
    .faculty-publications__year { margin-top: 2rem; }
    .faculty-publications__list { padding-left: 1.5rem; }
    .faculty-publications__item { margin-bottom: 1.25rem; }
    .faculty-publications__title { font-weight: 650; }
    .faculty-publications__details { margin: .25rem 0; color: #57606a; }
    .faculty-publications__badges { display: flex; gap: .5rem; flex-wrap: wrap; }
    .faculty-publications__badge { border: 1px solid #d0d7de; border-radius: 999px; padding: .15rem .5rem; font-size: .85rem; }
    a { color: #0969da; }
  </style>
</head>
<body>
<main>
{{fragment}}
</main>
</body>
</html>
""";
    }

    private static void AppendPublication(
        StringBuilder sb,
        AcademicOnlinePublicationItem publication)
    {
        var title = string.IsNullOrWhiteSpace(publication.Title)
            ? "Untitled publication"
            : publication.Title.Trim();

        var url = GetPreferredPublicationUrl(publication);

        sb.AppendLine("      <li class=\"faculty-publications__item\">");
        sb.Append("        <div class=\"faculty-publications__title\">");

        if (url is not null)
        {
            sb.Append("<a href=\"")
                .Append(EncodeAttribute(url))
                .Append("\" target=\"_blank\" rel=\"noopener\">")
                .Append(Encode(title))
                .Append("</a>");
        }
        else
        {
            sb.Append(Encode(title));
        }

        sb.AppendLine("</div>");

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(publication.Venue))
        {
            details.Add(Encode(publication.Venue.Trim()));
        }

        if (publication.PublicationYear.HasValue)
        {
            details.Add(publication.PublicationYear.Value.ToString());
        }
        else if (!string.IsNullOrWhiteSpace(publication.YearText))
        {
            details.Add(Encode(publication.YearText.Trim()));
        }

        var coauthors = ExtractCoauthorNames(publication.CoauthorsJson);
        if (coauthors.Count > 0)
        {
            details.Add("Coauthors: " + string.Join(", ", coauthors.Select(Encode)));
        }

        if (details.Count > 0)
        {
            sb.Append("        <p class=\"faculty-publications__details\">")
                .Append(string.Join(" · ", details))
                .AppendLine("</p>");
        }

        sb.AppendLine("        <div class=\"faculty-publications__badges\">");

        if (publication.Verified == true)
        {
            sb.AppendLine("          <span class=\"faculty-publications__badge\">Academic Online verified</span>");
        }

        if (publication.CitationCount.HasValue)
        {
            sb.Append("          <span class=\"faculty-publications__badge\">")
                .Append(publication.CitationCount.Value)
                .AppendLine(" citation(s)</span>");
        }

        if (!string.IsNullOrWhiteSpace(publication.Doi))
        {
            sb.Append("          <span class=\"faculty-publications__badge\">DOI: ")
                .Append(Encode(NormalizeDoiText(publication.Doi)))
                .AppendLine("</span>");
        }

        sb.AppendLine("        </div>");
        sb.AppendLine("      </li>");
    }

    private static string BuildProfileMeta(AcademicOnlineFacultyProfile profile)
    {
        var values = new List<string>();

        if (!string.IsNullOrWhiteSpace(profile.Position))
        {
            values.Add(Encode(profile.Position.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(profile.Department))
        {
            values.Add(Encode(profile.Department.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(profile.Institution))
        {
            values.Add(Encode(profile.Institution.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(profile.OrcidUrl) &&
            TrySafeHttpUrl(profile.OrcidUrl, out var orcidUrl))
        {
            values.Add($"<a href=\"{EncodeAttribute(orcidUrl)}\" target=\"_blank\" rel=\"noopener\">ORCID</a>");
        }

        return string.Join(" · ", values);
    }

    private static string? GetPreferredPublicationUrl(AcademicOnlinePublicationItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Doi))
        {
            var doiText = NormalizeDoiText(item.Doi);
            if (!string.IsNullOrWhiteSpace(doiText))
            {
                return "https://doi.org/" + Uri.EscapeDataString(doiText)
                    .Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (TrySafeHttpUrl(item.Link, out var link))
        {
            return link;
        }

        if (TrySafeHttpUrl(item.OpenAlexId, out var openAlex))
        {
            return openAlex;
        }

        if (!string.IsNullOrWhiteSpace(item.OpenAlexId))
        {
            var id = item.OpenAlexId.Trim();
            if (id.StartsWith("W", StringComparison.OrdinalIgnoreCase))
            {
                return "https://openalex.org/" + Uri.EscapeDataString(id);
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractCoauthorNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var names = new List<string>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                string? name = null;

                if (item.ValueKind == JsonValueKind.String)
                {
                    name = item.GetString();
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    name = TryGetString(item, "name")
                        ?? TryGetString(item, "display_name")
                        ?? TryGetString(item, "displayName");
                }

                if (!string.IsNullOrWhiteSpace(name) &&
                    !names.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(name.Trim());
                }
            }

            return names.Take(12).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string? TryGetString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static string NormalizeDoiText(string doi)
    {
        var value = doi.Trim();

        foreach (var prefix in new[]
                 {
                     "https://doi.org/",
                     "http://doi.org/",
                     "https://dx.doi.org/",
                     "http://dx.doi.org/",
                     "doi:"
                 })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..];
                break;
            }
        }

        return value.Trim();
    }

    private static bool TrySafeHttpUrl(string? value, out string url)
    {
        url = string.Empty;

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        url = uri.ToString();
        return true;
    }

    private static string Encode(string value) => Encoder.Encode(value);
    private static string EncodeAttribute(string value) => Encoder.Encode(value);
}
