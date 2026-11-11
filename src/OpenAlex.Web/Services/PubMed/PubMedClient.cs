using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Options;

namespace OpenAlex.Web.Services.PubMed;

/// <summary>Serializes dispatch and spaces requests below NCBI's unauthenticated limit.</summary>
public sealed class PubMedRequestGate : IDisposable
{
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly TimeSpan _spacing;
    private long _lastStart;

    public PubMedRequestGate(TimeSpan? spacing = null) => _spacing = spacing ?? TimeSpan.FromMilliseconds(400);

    public async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken token)
    {
        await _mutex.WaitAsync(token);
        try
        {
            if (_lastStart != 0)
            {
                var remaining = _spacing - Stopwatch.GetElapsedTime(_lastStart);
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token);
            }
            _lastStart = Stopwatch.GetTimestamp();
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        }
        finally { _mutex.Release(); }
    }

    public void Dispose() => _mutex.Dispose();
}

public sealed class PubMedClient : IPubMedClient
{
    private const string Endpoint = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/";
    private readonly HttpClient _http;
    private readonly PubMedRequestGate _gate;
    private readonly PubMedOptions _options;
    private readonly IConfiguration _configuration;

    public PubMedClient(HttpClient http, PubMedRequestGate gate,
        IOptions<PubMedOptions> options, IConfiguration configuration)
    {
        _http = http; _gate = gate; _options = options.Value; _configuration = configuration;
    }

    public async Task<PubMedSearchResponse> SearchAsync(PubMedQuery query, CancellationToken cancellationToken = default)
    {
        if (query.Limit is not (10 or 25 or 50) || query.Term.Length > 2000)
            throw new PubMedInputException("Use the supported bounded search options.");
        var email = string.IsNullOrWhiteSpace(_options.Email) ? _configuration["OpenAlex:MailTo"] : _options.Email;
        if (!MailAddress.TryCreate(email?.Trim(), out var contact)
            || !string.Equals(contact.Address, email?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new PubMedProviderException("PubMed lookup needs a valid application contact email. Ask the application administrator to complete the PubMed configuration.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        try
        {
            var searchJson = await GetAsync("esearch.fcgi", new Dictionary<string,string>
            {
                ["db"]="pubmed", ["retmode"]="json", ["retmax"]=query.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["sort"]="pub_date", ["term"]=query.Term
            }, contact.Address, token);
            using var document = JsonDocument.Parse(searchJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("esearchresult", out var search) || search.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("error", out _)
                || search.TryGetProperty("ERROR", out _))
                throw new PubMedProviderException("PubMed could not complete the search. Try again or edit the search terms.");
            if (!search.TryGetProperty("count", out var countElement)
                || !long.TryParse(countElement.ToString(), out var count) || count < 0
                || !search.TryGetProperty("idlist", out var idsElement) || idsElement.ValueKind != JsonValueKind.Array)
                throw new PubMedProviderException("PubMed returned an unreadable search response.");
            var ids = idsElement.EnumerateArray().Select(value =>
                value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty).ToArray();
            if (ids.Length > query.Limit || ids.Any(id => !Regex.IsMatch(id, @"^[1-9][0-9]{0,11}$")))
                throw new PubMedProviderException("PubMed returned unexpected citation identifiers.");
            ids = ids.Distinct(StringComparer.Ordinal).ToArray();
            if (count < ids.Length)
                throw new PubMedProviderException("PubMed returned an inconsistent search response. Please try again later.");
            if (count > 0 && ids.Length == 0)
                throw new PubMedProviderException("PubMed reported matches but returned no citation identifiers. Try a smaller search.");
            var translation = search.TryGetProperty("querytranslation", out var translated) ? translated.ToString() : query.Term;
            var notices = new List<string>();
            foreach (var section in new[] { "warninglist", "errorlist" })
                if (search.TryGetProperty(section, out var noticeSection) && noticeSection.ValueKind == JsonValueKind.Object)
                    foreach (var property in noticeSection.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Array)
                            notices.AddRange(property.Value.EnumerateArray().Select(value => property.Name + ": " + value));
                        else notices.Add(property.Name + ": " + property.Value);
                    }
            if (ids.Length == 0)
                return new PubMedSearchResponse(count, Array.Empty<PubMedArticle>(), translation,
                    notices.Take(10).Select(ShortNotice).ToArray(), DateTimeOffset.UtcNow);

            var xml = await GetAsync("efetch.fcgi", new Dictionary<string,string>
                { ["db"]="pubmed", ["retmode"]="xml", ["id"]=string.Join(",",ids) }, contact.Address, token);
            var fetched = PubMedXmlParser.Parse(xml);
            var byId = fetched.ToDictionary(article => article.Pmid, StringComparer.Ordinal);
            var articles = ids.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
            if (articles.Length != ids.Length)
                notices.Add("Some requested citations were not returned. This result is incomplete; review the PubMed search directly.");
            if (fetched.Any(article => !ids.Contains(article.Pmid, StringComparer.Ordinal)))
                notices.Add("Unexpected citation records were omitted from the response.");
            return new PubMedSearchResponse(count, articles, translation,
                notices.Take(10).Select(ShortNotice).ToArray(), DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PubMedProviderException("PubMed lookup timed out. Try fewer results or a narrower query.");
        }
        catch (Exception ex) when (ex is JsonException or XmlException or HttpRequestException)
        {
            throw new PubMedProviderException("PubMed data could not be retrieved or read. Try again later, or open the same search in PubMed.", ex);
        }
    }

    private async Task<string> GetAsync(string operation, Dictionary<string,string> parameters, string email, CancellationToken token)
    {
        parameters["tool"] = "OpenAlexFacultyPublications";
        parameters["email"] = email;
        var url = Endpoint + operation + "?" + string.Join("&", parameters.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("OpenAlexFacultyPublications/1.0");
        using var response = await _gate.SendAsync(_http, request, token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new PubMedProviderException("PubMed's request limit was reached. Please wait before trying again.");
        if (!response.IsSuccessStatusCode)
            throw new PubMedProviderException("PubMed is unavailable or refused the request. Try again later or open the search in PubMed.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(bytes, token)) > 0)
        {
            if (buffer.Length + read > 8_000_000)
                throw new PubMedProviderException("PubMed returned too much data. Reduce the result limit and try again.");
            buffer.Write(bytes, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string ShortNotice(string value) => value.Length <= 500 ? value : value[..500];
}
