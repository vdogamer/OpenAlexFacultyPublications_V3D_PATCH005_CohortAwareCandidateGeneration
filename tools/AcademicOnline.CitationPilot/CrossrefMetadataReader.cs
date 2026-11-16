using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AcademicOnline.CitationPilot;

internal sealed class CrossrefMetadataReader
{
    private readonly HttpClient _http;
    private readonly string? _mailTo;
    private readonly TimeSpan _spacing;
    private DateTimeOffset _lastRequest;
    public List<MetadataRequest> Requests { get; } = new();
    public bool Blocked { get; private set; }
    private const int MaxBodyBytes = 2_000_000;

    public CrossrefMetadataReader(HttpClient http, string? mailTo = null, TimeSpan? spacing = null)
    { _http = http; _mailTo = mailTo; _spacing = spacing ?? TimeSpan.FromMilliseconds(500); }

    public async Task<PublicationEntry> EnrichAsync(PublicationEntry entry, CancellationToken ct)
    {
        if (entry.Status != "MetadataNotRequested" || entry.NormalizedDoi is null)
            throw new InvalidOperationException("Only a valid, unheld, unique DOI can be enriched.");
        var doi = entry.NormalizedDoi;
        var url = "https://api.crossref.org/works/" + Uri.EscapeDataString(doi)
            + "/transform/application/vnd.citationstyles.csl+json";
        var started = DateTimeOffset.UtcNow;
        int? httpStatus = null;
        string? hash = null;
        try
        {
            var remaining = _spacing - (DateTimeOffset.UtcNow - _lastRequest);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct);
            ct.ThrowIfCancellationRequested();
            _lastRequest = DateTimeOffset.UtcNow;
            using var request = new HttpRequestMessage(HttpMethod.Get,
                url + (string.IsNullOrWhiteSpace(_mailTo) ? string.Empty : "?mailto=" + Uri.EscapeDataString(_mailTo)));
            request.Headers.UserAgent.ParseAdd("AcademicOnlineCitationPilot/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.citationstyles.csl+json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            httpStatus = (int)response.StatusCode;
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent or HttpStatusCode.NotAcceptable)
                return Finish("MetadataUnavailable", "Crossref did not supply CSL metadata. This does not establish that the DOI or publication is absent elsewhere.");
            if (!response.IsSuccessStatusCode)
            {
                Blocked = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500;
                return Finish("MetadataError", "Crossref refused or could not complete the request (HTTP " + httpStatus + ")."
                    + (Blocked ? " Remaining source requests were stopped." : string.Empty));
            }
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(bytes, timeout.Token)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes) return Finish("MetadataError", "The metadata response exceeded the two-megabyte limit.");
                buffer.Write(bytes, 0, read);
            }
            var body = buffer.ToArray();
            hash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
            var (csl, errors) = Parse(Encoding.UTF8.GetString(body), doi);
            var metadataSource = new MetadataSource("Crossref", url, DateTimeOffset.UtcNow, hash);
            if (errors.Length > 0) return Finish("MetadataIncomplete", string.Join(" ", errors), csl, metadataSource);
            return Finish("Ready", "Bibliographic metadata was retrieved; faculty authorship was not independently reviewed.", csl, metadataSource);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { return Finish("MetadataError", "Metadata lookup was cancelled. The completed SQL read was retained."); }
        catch (OperationCanceledException)
        { Blocked = true; return Finish("MetadataError", "Crossref timed out. Remaining source requests were stopped."); }
        catch (HttpRequestException)
        { Blocked = true; return Finish("MetadataError", "Crossref could not be reached. Remaining source requests were stopped."); }
        catch (IOException)
        { Blocked = true; return Finish("MetadataError", "Crossref's response was interrupted. Remaining source requests were stopped."); }
        catch (JsonException)
        { return Finish("MetadataError", "Crossref returned unreadable JSON. No citation fields were inferred."); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        { return Finish("MetadataError", "Crossref returned unsupported field values. No citation fields were inferred."); }

        PublicationEntry Finish(string status, string detail, JsonElement? csl = null, MetadataSource? source = null)
        {
            Requests.Add(new(doi, url, started, status, httpStatus, hash, detail));
            return entry with { Status = status, PreviewEligible = status == "Ready", Csl = csl,
                MetadataSource = source, Errors = status == "Ready" ? [] : [detail] };
        }
    }

    internal static (JsonElement? Csl, string[] Errors) Parse(string json, string expectedDoi)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return (null, ["The metadata response is not a CSL-JSON item."]);
        if (root.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
            return (null, ["The metadata response contains duplicate field names and was not used for a citation."]);
        string Text(string field) => root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty : string.Empty;
        var returnedDoi = DoiRules.Normalize(Text("DOI"));
        if (returnedDoi != expectedDoi)
            return (null, ["The metadata DOI does not match the stored DOI. No citation was included."]);
        var errors = new List<string>();
        var types = new HashSet<string>(StringComparer.Ordinal)
        { "article", "article-journal", "article-magazine", "article-newspaper", "book", "chapter", "paper-conference", "report", "dataset", "thesis", "manuscript", "review", "review-book", "software", "webpage", "entry", "entry-dictionary", "entry-encyclopedia", "standard" };
        var type = Text("type");
        if (!types.Contains(type)) errors.Add("A supported CSL publication type was not supplied.");
        if (Text("title").Length == 0) errors.Add("The publication title was not supplied.");
        if (!root.TryGetProperty("author", out var authors) || authors.ValueKind != JsonValueKind.Array || authors.GetArrayLength() == 0)
            errors.Add("The ordered author list was not supplied.");
        else
            foreach (var author in authors.EnumerateArray())
            {
                if (author.ValueKind != JsonValueKind.Object
                    || !new[] { "family", "literal" }.Any(field => author.TryGetProperty(field, out var value)
                        && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())))
                { errors.Add("An author lacks a usable family name or collective/literal name. Names were not guessed."); break; }
                foreach (var field in new[] { "family", "given", "literal", "suffix", "non-dropping-particle", "dropping-particle" })
                    if (author.TryGetProperty(field, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                        errors.Add("An author name field has an unsupported value type and was not guessed.");
            }
        foreach (var role in new[] { "editor", "translator", "container-author", "collection-editor", "composer", "director", "illustrator", "interviewer", "recipient", "reviewed-author", "original-author", "editorial-director", "chair", "compiler", "contributor", "curator", "executive-producer", "guest", "host", "narrator", "organizer", "performer", "producer", "script-writer", "series-creator" })
        {
            if (!root.TryGetProperty(role, out var people) || people.ValueKind == JsonValueKind.Null) continue;
            if (people.ValueKind != JsonValueKind.Array)
            { errors.Add("An optional contributor list has an unsupported value type."); continue; }
            foreach (var person in people.EnumerateArray())
            {
                if (person.ValueKind != JsonValueKind.Object
                    || !new[] { "family", "literal" }.Any(field => person.TryGetProperty(field, out var value)
                        && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())))
                { errors.Add("An optional contributor lacks a usable family or collective/literal name."); break; }
                foreach (var field in new[] { "family", "given", "literal", "suffix", "non-dropping-particle", "dropping-particle" })
                    if (person.TryGetProperty(field, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                        errors.Add("An optional contributor name field has an unsupported value type.");
            }
        }
        if (!root.TryGetProperty("issued", out var issued) || !ValidDate(issued))
            errors.Add("A usable publication year/date was not supplied. Deposited/indexed dates were not substituted.");
        foreach (var field in new[] { "accessed", "event-date", "original-date", "submitted" })
            if (root.TryGetProperty(field, out var date) && date.ValueKind != JsonValueKind.Null && !ValidDate(date))
                errors.Add("An optional bibliographic date has unsupported or invalid values.");
        foreach (var field in new[] { "title", "title-short", "container-title", "container-title-short", "journalAbbreviation", "publisher", "publisher-place", "collection-title", "event", "event-place", "genre", "status", "language", "ISBN", "ISSN", "URL" })
            if (root.TryGetProperty(field, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                errors.Add("A bibliographic text field has an unsupported value type.");
        foreach (var field in new[] { "volume", "issue", "page", "number", "edition", "chapter-number", "collection-number" })
            if (root.TryGetProperty(field, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null))
                errors.Add("A bibliographic number/page field has an unsupported value type.");
        if (type is "article-journal" or "article-magazine" or "article-newspaper" or "chapter" or "paper-conference"
            && Text("container-title").Length == 0)
            errors.Add("The journal, book or conference title was not supplied.");
        var node = JsonNode.Parse(json)!.AsObject();
        // Abstract text is not needed to render a reference and is not retained in this export.
        node.Remove("abstract");
        node["id"] = "doi:" + expectedDoi;
        node["DOI"] = expectedDoi;
        return (JsonSerializer.SerializeToElement(node), errors.ToArray());
    }

    private static bool ValidDate(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("date-parts", out var dates)
            || dates.ValueKind != JsonValueKind.Array || dates.GetArrayLength() is < 1 or > 2) return false;
        foreach (var date in dates.EnumerateArray())
        {
            if (date.ValueKind != JsonValueKind.Array || date.GetArrayLength() is < 1 or > 3) return false;
            var parts = new List<int>();
            foreach (var part in date.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Number || !part.TryGetInt32(out var number)) return false;
                parts.Add(number);
            }
            if (parts[0] is < 1000 or > 9999 || (parts.Count > 1 && parts[1] is < 1 or > 12)) return false;
            if (parts.Count > 2 && (parts[2] < 1 || parts[2] > DateTime.DaysInMonth(parts[0], parts[1]))) return false;
        }
        return true;
    }
}
