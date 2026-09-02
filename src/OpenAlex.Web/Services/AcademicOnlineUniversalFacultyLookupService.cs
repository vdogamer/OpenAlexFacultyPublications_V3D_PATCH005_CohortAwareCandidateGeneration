using System.Text.Json;
using System.Text.RegularExpressions;
using OpenAlex.Data;
using OpenAlex.OpenAlexClient;

namespace OpenAlex.Web.Services;

public sealed class AcademicOnlineUniversalFacultyLookupService
{
    private static readonly Regex UvaUserIdPattern =
        new("^[A-Za-z0-9]{2,32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AcademicOnlineApiClient _client;
    private readonly PublicationKeywordFilterService _keywordFilter;

    public AcademicOnlineUniversalFacultyLookupService(
        AcademicOnlineApiClient client,
        PublicationKeywordFilterService keywordFilter)
    {
        _client = client;
        _keywordFilter = keywordFilter;
    }

    public Task<IReadOnlyList<AcademicOnlineScholarSearchCandidate>> SearchAsync(
        string searchText,
        string? departmentName,
        CancellationToken cancellationToken = default)
    {
        return _client.SearchScholarCandidatesAsync(
            searchText,
            departmentName,
            rowsPerPage: 25,
            cancellationToken);
    }

    public async Task<IReadOnlyList<AcademicOnlineScholarSearchCandidate>> SearchByUvaUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var normalizedUserId = NormalizeUvaUserId(userId);
        var expectedEmail = normalizedUserId + "@virginia.edu";

        var byEmail = await _client.SearchScholarCandidatesAsync(
            expectedEmail,
            departmentName: null,
            rowsPerPage: 25,
            cancellationToken);

        var exactMatches = byEmail
            .Where(candidate =>
                string.Equals(
                    candidate.Email?.Trim(),
                    expectedEmail,
                    StringComparison.OrdinalIgnoreCase))
            .GroupBy(candidate => candidate.AuthorId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (exactMatches.Count > 0)
        {
            return exactMatches;
        }

        // Some Academic Online deployments may index the computing ID independently
        // of the full email address. Try the normalized ID as a discovery fallback,
        // but still require the returned scholar email to match exactly.
        var byUserId = await _client.SearchScholarCandidatesAsync(
            normalizedUserId,
            departmentName: null,
            rowsPerPage: 25,
            cancellationToken);

        return byUserId
            .Where(candidate =>
                string.Equals(
                    candidate.Email?.Trim(),
                    expectedEmail,
                    StringComparison.OrdinalIgnoreCase))
            .GroupBy(candidate => candidate.AuthorId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public static string NormalizeUvaUserId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Enter a UVA User ID.");
        }

        var trimmed = value.Trim();

        if (trimmed.EndsWith(
                "@virginia.edu",
                StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^"@virginia.edu".Length];
        }

        if (!UvaUserIdPattern.IsMatch(trimmed))
        {
            throw new InvalidOperationException(
                "UVA User ID must contain only letters and numbers.");
        }

        return trimmed.ToLowerInvariant();
    }

    public Task<AcademicOnlineUniversalFacultyResult> GetPublicationsAsync(
        string academicOnlineAuthorId,
        bool verifiedOnly,
        int? fromYear,
        int? toYear,
        CancellationToken cancellationToken)
    {
        return GetPublicationsAsync(
            academicOnlineAuthorId,
            verifiedOnly,
            fromYear,
            toYear,
            keywordQuery: null,
            keywordMatchMode: null,
            cancellationToken: cancellationToken);
    }

    public async Task<AcademicOnlineUniversalFacultyResult> GetPublicationsAsync(
        string academicOnlineAuthorId,
        bool verifiedOnly,
        int? fromYear,
        int? toYear,
        string? keywordQuery = null,
        string? keywordMatchMode = null,
        CancellationToken cancellationToken = default)
    {
        var authorId = ValidateAuthorId(academicOnlineAuthorId);

        using var general = await _client.GetAuthorGeneralAsync(
            authorId,
            cancellationToken);

        var publications = await _client.GetAllAuthorPublicationsAsync(
            authorId,
            verifiedOnly,
            fromYear: fromYear,
            toYear: toYear,
            cancellationToken: cancellationToken);

        var profile = ParseProfile(general.RootElement);

        var filtered = _keywordFilter.Apply(
            publications.Publications,
            keywordQuery,
            keywordMatchMode);

        var renderResult = new AcademicOnlineFacultyPublicationsResult
        {
            Mapping = new FacultyAcademicOnlineMappingRow
            {
                FacultyDisplayName = profile.Name,
                AcademicOnlineAuthorId = authorId,
                AcademicOnlineName = profile.Name,
                AcademicOnlineEmail = profile.Email,
                AcademicOnlineOrcidUrl = profile.OrcidUrl,
                MappingSource = "UniversalAcademicOnlineLookup"
            },
            Profile = profile,
            Publications = filtered.Publications,
            UnfilteredPublicationCount = filtered.SourcePublicationCount,
            KeywordQuery = filtered.KeywordQuery,
            KeywordMatchMode = filtered.MatchMode,
            KeywordTerms = filtered.Terms,
            PagesFetched = publications.PagesFetched,
            RowsPerPage = publications.RowsPerPage,
            HitMaxPages = publications.HitMaxPages,
            VerifiedOnly = verifiedOnly,
            FromYear = fromYear,
            ToYear = toYear
        };

        return new AcademicOnlineUniversalFacultyResult
        {
            AcademicOnlineAuthorId = authorId,
            RenderResult = renderResult
        };
    }

    private static string ValidateAuthorId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "Choose an Academic Online scholar before loading publications.");
        }

        var trimmed = value.Trim();

        if (trimmed.Length > 128 ||
            trimmed.Any(character =>
                !(char.IsLetterOrDigit(character) ||
                  character == '-' ||
                  character == '_' ||
                  character == '.')))
        {
            throw new InvalidOperationException(
                "The selected Academic Online scholar ID is not valid.");
        }

        return trimmed;
    }

    private static AcademicOnlineFacultyProfile ParseProfile(JsonElement root)
    {
        return new AcademicOnlineFacultyProfile
        {
            Name = GetString(root, "name") ?? "Academic Online Scholar",
            Department = GetString(root, "department"),
            Institution = GetString(root, "institution"),
            Email = GetString(root, "email"),
            Position = GetString(root, "position"),
            OrcidUrl = GetString(root, "orcid_url"),
            PublicationCount = GetInt(root, "publication_count"),
            HasPublications = GetBool(root, "has_publications"),
            HasVerifiedPublications = GetBool(root, "has_verified_publications")
        };
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

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.ToString(), out var parsed)
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

        return bool.TryParse(value.ToString(), out var parsed)
            ? parsed
            : null;
    }
}

public sealed class AcademicOnlineUniversalFacultyResult
{
    public string AcademicOnlineAuthorId { get; init; } = string.Empty;
    public AcademicOnlineFacultyPublicationsResult RenderResult { get; init; } = new();
}
