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

    public async Task<IReadOnlyList<AcademicOnlineScholarSearchCandidate>> SearchScholarCandidatesAsync(
        string searchText,
        string? departmentName = null,
        int rowsPerPage = 25,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return Array.Empty<AcademicOnlineScholarSearchCandidate>();
        }

        using var document = await SearchScholarsAsync(
            searchText.Trim(),
            departmentName,
            page: 0,
            rowsPerPage: Math.Clamp(rowsPerPage, 1, 100),
            cancellationToken);

        if (!document.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AcademicOnlineScholarSearchCandidate>();
        }

        var candidates = new List<AcademicOnlineScholarSearchCandidate>();

        foreach (var item in results.EnumerateArray())
        {
            var authorId = GetString(item, "id");
            if (string.IsNullOrWhiteSpace(authorId))
            {
                continue;
            }

            candidates.Add(new AcademicOnlineScholarSearchCandidate
            {
                AuthorId = authorId.Trim(),
                Name = GetString(item, "name"),
                Email = GetString(item, "email"),
                Position = GetString(item, "position"),
                Department =
                    AcademicOnlineDepartmentParser.GetPreferredDepartmentName(item),
                Institution =
                    GetString(item, "institution")
                    ?? GetString(item, "institution_name"),
                OrcidUrl =
                    GetString(item, "orcid_url")
                    ?? GetString(item, "orcid"),
                RankingScore = GetDecimal(item, "ranking_score")
            });
        }

        return candidates
            .OrderByDescending(candidate => candidate.RankingScore ?? decimal.MinValue)
            .ThenBy(candidate => candidate.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AcademicOnlineDepartmentDiscoveryCollection>
        DiscoverDepartmentsAsync(
            string parentName,
            int rowsPerPage = 100,
            int maxPages = 100,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentName))
        {
            throw new ArgumentException(
                "Academic Online parent organization name is required.",
                nameof(parentName));
        }

        var normalizedParentName = parentName.Trim();
        var pageSize = Math.Clamp(rowsPerPage, 1, 100);
        var pageLimit = Math.Clamp(maxPages, 1, 1000);

        var seenAuthorIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var accumulators =
            new Dictionary<string, DepartmentDiscoveryAccumulator>(
                StringComparer.OrdinalIgnoreCase);

        var pagesFetched = 0;
        var affiliationsObserved = 0;
        var hitMaxPages = false;

        for (var page = 0; page < pageLimit; page++)
        {
            using var document = await SearchScholarsAsync(
                facultyDisplayName: string.Empty,
                departmentName: null,
                page: page,
                rowsPerPage: pageSize,
                cancellationToken: cancellationToken);

            pagesFetched++;

            if (!document.RootElement.TryGetProperty(
                    "results",
                    out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var pageRowCount = 0;
            var uniqueBefore = seenAuthorIds.Count;

            foreach (var item in results.EnumerateArray())
            {
                pageRowCount++;

                var authorId = GetString(item, "id");
                if (string.IsNullOrWhiteSpace(authorId))
                {
                    continue;
                }

                var normalizedAuthorId = authorId.Trim();
                if (!seenAuthorIds.Add(normalizedAuthorId))
                {
                    continue;
                }

                var affiliations =
                    AcademicOnlineDepartmentParser
                        .GetDepartmentAffiliations(item);

                affiliationsObserved += affiliations.Count;

                foreach (var affiliation in affiliations)
                {
                    var key = BuildDepartmentDiscoveryKey(
                        affiliation);

                    if (!accumulators.TryGetValue(
                            key,
                            out var accumulator))
                    {
                        accumulator =
                            new DepartmentDiscoveryAccumulator(
                                affiliation);

                        accumulators.Add(
                            key,
                            accumulator);
                    }

                    accumulator.AuthorIds.Add(
                        normalizedAuthorId);
                }
            }

            var newUniqueAuthors =
                seenAuthorIds.Count - uniqueBefore;

            if (pageRowCount == 0 ||
                newUniqueAuthors == 0)
            {
                break;
            }

            if (page == pageLimit - 1)
            {
                hitMaxPages = true;
            }
        }

        var allAffiliations =
            accumulators.Values
                .Select(item => item.Affiliation)
                .ToList();

        var parentIds =
            allAffiliations
                .Where(item =>
                    string.Equals(
                        item.Name,
                        normalizedParentName,
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Id))
                .Select(item => item.Id!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var parentSlugs =
            allAffiliations
                .Where(item =>
                    string.Equals(
                        item.Name,
                        normalizedParentName,
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Slug))
                .Select(item => item.Slug!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var departments =
            accumulators.Values
                .Where(item =>
                {
                    var affiliation = item.Affiliation;

                    if (string.Equals(
                            affiliation.Name,
                            normalizedParentName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return
                        string.Equals(
                            affiliation.ParentName,
                            normalizedParentName,
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        (!string.IsNullOrWhiteSpace(
                             affiliation.ParentId) &&
                         parentIds.Contains(
                             affiliation.ParentId))
                        ||
                        (!string.IsNullOrWhiteSpace(
                             affiliation.ParentSlug) &&
                         parentSlugs.Contains(
                             affiliation.ParentSlug));
                })
                .Select(item =>
                    new AcademicOnlineDepartmentDiscoveryItem
                    {
                        AcademicOnlineDepartmentId =
                            item.Affiliation.Id,
                        DepartmentName =
                            item.Affiliation.Name,
                        Slug =
                            item.Affiliation.Slug,
                        ParentAcademicOnlineDepartmentId =
                            item.Affiliation.ParentId,
                        ParentName =
                            item.Affiliation.ParentName
                            ?? normalizedParentName,
                        ParentSlug =
                            item.Affiliation.ParentSlug,
                        ScholarCount =
                            item.AuthorIds.Count
                    })
                .OrderBy(
                    item => item.DepartmentName,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        return new AcademicOnlineDepartmentDiscoveryCollection
        {
            Departments = departments,
            PagesFetched = pagesFetched,
            RowsPerPage = pageSize,
            ScholarsScanned = seenAuthorIds.Count,
            AffiliationsObserved = affiliationsObserved,
            HitMaxPages = hitMaxPages
        };
    }

    public async Task<AcademicOnlineDepartmentScholarCollection> GetDepartmentScholarsAsync(
        string departmentName,
        int rowsPerPage = 100,
        int maxPages = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(departmentName))
        {
            throw new ArgumentException(
                "Academic Online department name is required.",
                nameof(departmentName));
        }

        var pageSize = Math.Clamp(rowsPerPage, 1, 100);
        var pageLimit = Math.Clamp(maxPages, 1, 1000);
        var scholars = new List<AcademicOnlineScholarSearchCandidate>();
        var seenAuthorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pagesFetched = 0;
        var hitMaxPages = false;

        for (var page = 0; page < pageLimit; page++)
        {
            using var document = await SearchScholarsAsync(
                facultyDisplayName: string.Empty,
                departmentName: departmentName.Trim(),
                page: page,
                rowsPerPage: pageSize,
                cancellationToken: cancellationToken);

            pagesFetched++;

            if (!document.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var pageRowCount = 0;
            var uniqueBefore = scholars.Count;

            foreach (var item in results.EnumerateArray())
            {
                pageRowCount++;

                var authorId = GetString(item, "id");
                if (string.IsNullOrWhiteSpace(authorId) ||
                    !seenAuthorIds.Add(authorId.Trim()))
                {
                    continue;
                }

                scholars.Add(new AcademicOnlineScholarSearchCandidate
                {
                    AuthorId = authorId.Trim(),
                    Name = GetString(item, "name"),
                    Email = GetString(item, "email"),
                    Position = GetString(item, "position"),
                    Department =
                        AcademicOnlineDepartmentParser
                            .GetPreferredDepartmentName(item),
                    Institution =
                        GetString(item, "institution")
                        ?? GetString(item, "institution_name"),
                    OrcidUrl =
                        GetString(item, "orcid_url")
                        ?? GetString(item, "orcid"),
                    RankingScore = GetDecimal(item, "ranking_score")
                });
            }

            var newUniqueRows = scholars.Count - uniqueBefore;
            if (pageRowCount == 0 || newUniqueRows == 0)
            {
                break;
            }

            if (page == pageLimit - 1)
            {
                hitMaxPages = true;
            }
        }

        return new AcademicOnlineDepartmentScholarCollection
        {
            Scholars = scholars
                .OrderBy(item => item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PagesFetched = pagesFetched,
            RowsPerPage = pageSize,
            HitMaxPages = hitMaxPages
        };
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

    public async Task<AcademicOnlinePublicationCollection> GetAllAuthorPublicationsAsync(
        string authorId,
        bool verifiedOnly,
        int? rowsPerPage = null,
        int? maxPages = null,
        int? fromYear = null,
        int? toYear = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authorId))
        {
            throw new ArgumentException("Academic Online authorId is required.", nameof(authorId));
        }

        var pageSize = Math.Clamp(rowsPerPage ?? _options.PublicationRowsPerPage, 1, 100);
        var pageLimit = Math.Clamp(maxPages ?? _options.MaxPublicationPages, 1, 1000);

        var publications = new List<AcademicOnlinePublicationItem>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pagesFetched = 0;
        var hitMaxPages = false;

        for (var page = 0; page < pageLimit; page++)
        {
            using var document = await GetAuthorPublicationsAsync(
                authorId,
                verifiedOnly,
                page,
                pageSize,
                fromYear,
                toYear,
                cancellationToken);

            pagesFetched++;

            if (!document.RootElement.TryGetProperty("publications", out var array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "Academic Online author/publications response did not contain a publications array.");
            }

            var pageCount = 0;
            var publicationCountBeforePage = publications.Count;

            foreach (var publication in array.EnumerateArray())
            {
                pageCount++;

                var item = ParsePublication(publication);
                var key = BuildPublicationDeduplicationKey(item, page, pageCount);

                if (seenKeys.Add(key))
                {
                    publications.Add(item);
                }
            }

            var newUniquePublications = publications.Count - publicationCountBeforePage;

            // Do not assume the service honors the requested page size. Continue until
            // Academic Online returns no rows or stops yielding new unique publications.
            if (pageCount == 0 || newUniquePublications == 0)
            {
                break;
            }

            if (page == pageLimit - 1)
            {
                hitMaxPages = true;
            }
        }

        return new AcademicOnlinePublicationCollection
        {
            Publications = publications
                .OrderByDescending(item => item.PublicationYear ?? int.MinValue)
                .ThenBy(item => item.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PagesFetched = pagesFetched,
            RowsPerPage = pageSize,
            HitMaxPages = hitMaxPages
        };
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

    private static AcademicOnlinePublicationItem ParsePublication(JsonElement publication)
    {
        var yearText = GetString(publication, "year");

        return new AcademicOnlinePublicationItem
        {
            Id = GetString(publication, "id"),
            Title = GetString(publication, "title"),
            Venue = GetString(publication, "venue"),
            YearText = yearText,
            PublicationYear = int.TryParse(yearText, out var year) ? year : null,
            CitationCount = GetInt(publication, "num_citations"),
            Doi = GetString(publication, "doi"),
            OpenAlexId = GetString(publication, "openalex_id"),
            Verified = GetBool(publication, "verified"),
            Link = GetString(publication, "link"),
            CoauthorsJson = GetRawJson(publication, "coauthors"),
            KeywordsJson = GetRawJson(publication, "keywords"),
            RawJson = publication.GetRawText()
        };
    }

    private static string BuildPublicationDeduplicationKey(
        AcademicOnlinePublicationItem item,
        int page,
        int ordinal)
    {
        if (!string.IsNullOrWhiteSpace(item.Id))
        {
            return "id:" + item.Id.Trim();
        }

        if (!string.IsNullOrWhiteSpace(item.OpenAlexId))
        {
            return "openalex:" + item.OpenAlexId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(item.Doi))
        {
            return "doi:" + item.Doi.Trim();
        }

        if (!string.IsNullOrWhiteSpace(item.Title))
        {
            return "title-year:" + item.Title.Trim() + "|" + (item.PublicationYear?.ToString() ?? item.YearText ?? string.Empty);
        }

        return $"page-ordinal:{page}:{ordinal}";
    }

    private static string BuildDepartmentDiscoveryKey(
        AcademicOnlineDepartmentAffiliation affiliation)
    {
        if (!string.IsNullOrWhiteSpace(affiliation.Id))
        {
            return "id:" + affiliation.Id.Trim();
        }

        if (!string.IsNullOrWhiteSpace(affiliation.Slug))
        {
            return "slug:" + affiliation.Slug.Trim();
        }

        return "name:" + affiliation.Name.Trim();
    }

    private sealed class DepartmentDiscoveryAccumulator
    {
        public DepartmentDiscoveryAccumulator(
            AcademicOnlineDepartmentAffiliation affiliation)
        {
            Affiliation = affiliation;
        }

        public AcademicOnlineDepartmentAffiliation Affiliation
        {
            get;
        }

        public HashSet<string> AuthorIds { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.TryParse(value.ToString(), out var parsed)
            ? parsed
            : null;
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static string? GetRawJson(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return value.GetRawText();
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
