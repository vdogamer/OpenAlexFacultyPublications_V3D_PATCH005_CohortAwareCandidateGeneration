using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class AcademicOnlineComparisonJob
{
    private readonly AcademicOnlineApiClient _client;
    private readonly CandidateGenerationRepository _candidateGenerationRepository;
    private readonly AcademicOnlineComparisonRepository _repository;
    private readonly IOperationalLogger _logger;
    private readonly AcademicOnlineComparisonOptions _options;

    public AcademicOnlineComparisonJob(
        AcademicOnlineApiClient client,
        CandidateGenerationRepository candidateGenerationRepository,
        AcademicOnlineComparisonRepository repository,
        IOperationalLogger logger,
        IOptions<AcademicOnlineComparisonOptions> options)
    {
        _client = client;
        _candidateGenerationRepository = candidateGenerationRepository;
        _repository = repository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var cohortName = string.IsNullOrWhiteSpace(_options.PilotCohortName) ? null : _options.PilotCohortName.Trim();
        var maxFaculty = Math.Max(1, _options.MaxFacultyToProcess);
        var rowsPerPage = Math.Clamp(_options.RowsPerPage, 1, 100);

        var runId = await _repository.StartRunAsync(_options.RunMode, cohortName, maxFaculty, _options.StartedBy, _options.ApplicationVersion, cancellationToken);

        try
        {
            var faculties = cohortName is null
                ? await _candidateGenerationRepository.LoadFacultyAsync(maxFaculty, cancellationToken)
                : await _candidateGenerationRepository.LoadFacultyForPilotCohortAsync(cohortName, maxFaculty, cancellationToken);

            var facultyProcessed = 0;
            var scholarsFound = 0;
            var publicationRowsInserted = 0;
            var verifiedPublicationRowsInserted = 0;
            var facultyWithNoScholar = new List<long>();

            foreach (var faculty in faculties)
            {
                facultyProcessed++;
                using var searchDocument = await _client.SearchScholarsAsync(faculty.DisplayName, faculty.DepartmentName, 0, rowsPerPage, cancellationToken);
                var searchRawJson = searchDocument.RootElement.GetRawText();
                var firstScholar = GetFirstArrayItem(searchDocument.RootElement, "results");

                var authorId = firstScholar.HasValue ? GetString(firstScholar.Value, "id") : null;
                var scholarName = firstScholar.HasValue ? GetString(firstScholar.Value, "name") : null;
                var scholarEmail = firstScholar.HasValue ? GetString(firstScholar.Value, "email") : null;
                var scholarPosition = firstScholar.HasValue ? GetString(firstScholar.Value, "position") : null;
                var searchRankingScore = firstScholar.HasValue ? GetDecimal(firstScholar.Value, "ranking_score") : null;

                string? generalRawJson = null;
                string? orcidUrl = null;
                int? publicationCount = null;
                bool? hasVerifiedPublications = null;

                if (!string.IsNullOrWhiteSpace(authorId))
                {
                    scholarsFound++;
                    using var generalDocument = await _client.GetAuthorGeneralAsync(authorId, cancellationToken);
                    generalRawJson = generalDocument.RootElement.GetRawText();
                    scholarName = GetString(generalDocument.RootElement, "name") ?? scholarName;
                    scholarEmail = GetString(generalDocument.RootElement, "email") ?? scholarEmail;
                    scholarPosition = GetString(generalDocument.RootElement, "position") ?? scholarPosition;
                    orcidUrl = GetString(generalDocument.RootElement, "orcid_url");
                    publicationCount = GetInt(generalDocument.RootElement, "publication_count");
                    hasVerifiedPublications = GetBool(generalDocument.RootElement, "has_verified_publications");

                    using var allPublications = await _client.GetAuthorPublicationsAsync(authorId, false, 0, rowsPerPage, _options.FromYear, _options.ToYear, cancellationToken);
                    publicationRowsInserted += await SavePublicationSnapshotsAsync(runId, faculty.FacultyId, authorId, false, allPublications.RootElement, cancellationToken);

                    using var verifiedPublications = await _client.GetAuthorPublicationsAsync(authorId, true, 0, rowsPerPage, _options.FromYear, _options.ToYear, cancellationToken);
                    verifiedPublicationRowsInserted += await SavePublicationSnapshotsAsync(runId, faculty.FacultyId, authorId, true, verifiedPublications.RootElement, cancellationToken);
                }
                else
                {
                    facultyWithNoScholar.Add(faculty.FacultyId);
                }

                await _repository.SaveScholarSnapshotAsync(runId, faculty.FacultyId, faculty.DisplayName, faculty.DepartmentName, authorId, scholarName, scholarEmail, scholarPosition, orcidUrl, publicationCount, hasVerifiedPublications, searchRankingScore, searchRawJson, generalRawJson, cancellationToken);
            }

            var summary = $"Academic Online comparison completed. FacultyProcessed={facultyProcessed}; ScholarsFound={scholarsFound}; PublicationRowsInserted={publicationRowsInserted}; VerifiedPublicationRowsInserted={verifiedPublicationRowsInserted}; FacultyWithNoScholar={FormatIdList(facultyWithNoScholar)}.";
            await _repository.CompleteRunAsync(runId, summary, cancellationToken);
            await _logger.LogInfoAsync("AcademicOnlineComparison", "RunCompleted", summary, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await _repository.FailRunAsync(runId, ex, CancellationToken.None);
            await _logger.LogErrorAsync("AcademicOnlineComparison", "RunFailed", $"Academic Online comparison run {runId} failed.", ex, cancellationToken: CancellationToken.None);
            throw;
        }
    }

    private async Task<int> SavePublicationSnapshotsAsync(long runId, long facultyId, string authorId, bool verifiedQuery, JsonElement root, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("publications", out var publications) || publications.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var inserted = 0;
        foreach (var publication in publications.EnumerateArray())
        {
            var yearText = GetString(publication, "year");
            int? publicationYear = int.TryParse(yearText, out var parsedYear) ? parsedYear : null;

            await _repository.SavePublicationSnapshotAsync(runId, facultyId, authorId, verifiedQuery, GetString(publication, "id"), GetString(publication, "title"), GetString(publication, "venue"), yearText, publicationYear, GetInt(publication, "num_citations"), GetString(publication, "doi"), GetString(publication, "openalex_id"), GetBool(publication, "verified"), GetString(publication, "link"), GetRawJson(publication, "coauthors"), GetRawJson(publication, "keywords"), publication.GetRawText(), cancellationToken);
            inserted++;
        }

        return inserted;
    }

    private static JsonElement? GetFirstArrayItem(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in array.EnumerateArray()) return item;
        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static string? GetRawJson(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.GetRawText();
    }

    private static string FormatIdList(IEnumerable<long> values)
    {
        var ordered = values.OrderBy(value => value).ToList();
        return ordered.Count == 0 ? "(none)" : string.Join(",", ordered);
    }
}
