using System.Text.Json;
using OpenAlex.Data;
using OpenAlex.OpenAlexClient;

namespace OpenAlex.Web.Services;

public sealed class AcademicOnlineFacultyPublicationsService
{
    private readonly FacultyAcademicOnlineMappingRepository _mappingRepository;
    private readonly AcademicOnlineApiClient _academicOnlineClient;
    private readonly PublicationKeywordFilterService _keywordFilter;

    public AcademicOnlineFacultyPublicationsService(
        FacultyAcademicOnlineMappingRepository mappingRepository,
        AcademicOnlineApiClient academicOnlineClient,
        PublicationKeywordFilterService keywordFilter)
    {
        _mappingRepository = mappingRepository;
        _academicOnlineClient = academicOnlineClient;
        _keywordFilter = keywordFilter;
    }

    public async Task<AcademicOnlineFacultyPublicationsResult> GetAsync(
        long facultyId,
        bool verifiedOnly,
        int? fromYear,
        int? toYear,
        string? keywordQuery = null,
        string? keywordMatchMode = null,
        CancellationToken cancellationToken = default)
    {
        var mapping = await _mappingRepository.GetCurrentMappingAsync(
            facultyId,
            cancellationToken);

        if (mapping is null)
        {
            throw new InvalidOperationException(
                $"FacultyId {facultyId} was not found in openalex.Faculty.");
        }

        if (!mapping.HasCurrentMapping)
        {
            throw new InvalidOperationException(
                $"FacultyId {facultyId} ({mapping.FacultyDisplayName}) has no current Academic Online mapping. Create and verify a mapping before generating production HTML.");
        }

        using var general = await _academicOnlineClient.GetAuthorGeneralAsync(
            mapping.AcademicOnlineAuthorId!,
            cancellationToken);

        var publications = await _academicOnlineClient.GetAllAuthorPublicationsAsync(
            mapping.AcademicOnlineAuthorId!,
            verifiedOnly,
            fromYear: fromYear,
            toYear: toYear,
            cancellationToken: cancellationToken);

        var profile = ParseGeneralProfile(
            general.RootElement,
            mapping);

        var filtered = _keywordFilter.Apply(
            publications.Publications,
            keywordQuery,
            keywordMatchMode);

        return new AcademicOnlineFacultyPublicationsResult
        {
            Mapping = mapping,
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
    }

    private static AcademicOnlineFacultyProfile ParseGeneralProfile(
        JsonElement root,
        FacultyAcademicOnlineMappingRow mapping)
    {
        return new AcademicOnlineFacultyProfile
        {
            Name = GetString(root, "name")
                ?? mapping.AcademicOnlineName
                ?? mapping.FacultyDisplayName,
            Department = GetString(root, "department")
                ?? mapping.DepartmentName,
            Institution = GetString(root, "institution"),
            Email = GetString(root, "email")
                ?? mapping.AcademicOnlineEmail
                ?? mapping.FacultyEmail,
            Position = GetString(root, "position"),
            OrcidUrl = GetString(root, "orcid_url")
                ?? mapping.AcademicOnlineOrcidUrl,
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

public sealed class AcademicOnlineFacultyPublicationsResult
{
    public FacultyAcademicOnlineMappingRow Mapping { get; init; } = new();
    public AcademicOnlineFacultyProfile Profile { get; init; } = new();
    public IReadOnlyList<AcademicOnlinePublicationItem> Publications { get; init; } =
        Array.Empty<AcademicOnlinePublicationItem>();

    public int UnfilteredPublicationCount { get; init; }
    public string? KeywordQuery { get; init; }
    public string KeywordMatchMode { get; init; } = "any";
    public IReadOnlyList<string> KeywordTerms { get; init; } = Array.Empty<string>();

    public int PagesFetched { get; init; }
    public int RowsPerPage { get; init; }
    public bool HitMaxPages { get; init; }
    public bool VerifiedOnly { get; init; }
    public int? FromYear { get; init; }
    public int? ToYear { get; init; }
}

public sealed class AcademicOnlineFacultyProfile
{
    public string Name { get; init; } = string.Empty;
    public string? Department { get; init; }
    public string? Institution { get; init; }
    public string? Email { get; init; }
    public string? Position { get; init; }
    public string? OrcidUrl { get; init; }
    public int? PublicationCount { get; init; }
    public bool? HasPublications { get; init; }
    public bool? HasVerifiedPublications { get; init; }
}
