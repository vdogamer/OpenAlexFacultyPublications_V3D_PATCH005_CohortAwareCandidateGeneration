using System.Text.Json;
using OpenAlex.Data;
using OpenAlex.OpenAlexClient;

namespace OpenAlex.Web.Services;

public sealed class AcademicOnlineUniversalFacultyLookupService
{
    private readonly AcademicOnlineApiClient _client;

    public AcademicOnlineUniversalFacultyLookupService(
        AcademicOnlineApiClient client)
    {
        _client = client;
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

    public async Task<AcademicOnlineUniversalFacultyResult> GetPublicationsAsync(
        string academicOnlineAuthorId,
        bool verifiedOnly,
        int? fromYear,
        int? toYear,
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
            Publications = publications.Publications,
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
