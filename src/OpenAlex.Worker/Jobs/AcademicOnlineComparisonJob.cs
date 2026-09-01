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
    private readonly FacultyAcademicOnlineMappingRepository _mappingRepository;
    private readonly IOperationalLogger _logger;
    private readonly AcademicOnlineComparisonOptions _options;

    public AcademicOnlineComparisonJob(
        AcademicOnlineApiClient client,
        CandidateGenerationRepository candidateGenerationRepository,
        AcademicOnlineComparisonRepository repository,
        FacultyAcademicOnlineMappingRepository mappingRepository,
        IOperationalLogger logger,
        IOptions<AcademicOnlineComparisonOptions> options)
    {
        _client = client;
        _candidateGenerationRepository = candidateGenerationRepository;
        _repository = repository;
        _mappingRepository = mappingRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var cohortName = string.IsNullOrWhiteSpace(_options.PilotCohortName)
            ? null
            : _options.PilotCohortName.Trim();

        var maxFaculty = Math.Max(1, _options.MaxFacultyToProcess);
        var rowsPerPage = Math.Clamp(_options.RowsPerPage, 1, 100);

        var runId = await _repository.StartRunAsync(
            _options.RunMode,
            cohortName,
            maxFaculty,
            _options.StartedBy,
            _options.ApplicationVersion,
            cancellationToken);

        try
        {
            var faculties = cohortName is null
                ? await _candidateGenerationRepository.LoadFacultyAsync(
                    maxFaculty,
                    cancellationToken)
                : await _candidateGenerationRepository.LoadFacultyForPilotCohortAsync(
                    cohortName,
                    maxFaculty,
                    cancellationToken);

            var facultyProcessed = 0;
            var scholarsFound = 0;
            var publicationRowsInserted = 0;
            var verifiedPublicationRowsInserted = 0;
            var allPublicationPagesFetched = 0;
            var verifiedPublicationPagesFetched = 0;
            var paginationSafeguardHitCount = 0;
            var facultyWithNoScholar = new List<long>();

            foreach (var faculty in faculties)
            {
                facultyProcessed++;

                var mapping = await _mappingRepository.GetCurrentMappingAsync(
                    faculty.FacultyId,
                    cancellationToken);

                string searchRawJson;
                string? authorId;
                string? scholarName;
                string? scholarEmail;
                string? scholarPosition;
                decimal? searchRankingScore;

                if (mapping?.HasCurrentMapping == true)
                {
                    authorId = mapping.AcademicOnlineAuthorId;
                    scholarName = mapping.AcademicOnlineName;
                    scholarEmail = mapping.AcademicOnlineEmail;
                    scholarPosition = null;
                    searchRankingScore = null;

                    searchRawJson = JsonSerializer.Serialize(new
                    {
                        resolution = "DurableCurrentMapping",
                        mapping.FacultyAcademicOnlineAuthorMapId,
                        mapping.FacultyId,
                        mapping.AcademicOnlineAuthorId,
                        mapping.MappingSource,
                        mapping.VerifiedBy,
                        mapping.VerifiedAtUtc
                    });
                }
                else
                {
                    using var searchDocument = await _client.SearchScholarsAsync(
                        faculty.DisplayName,
                        faculty.DepartmentName,
                        0,
                        rowsPerPage,
                        cancellationToken);

                    searchRawJson = searchDocument.RootElement.GetRawText();
                    var firstScholar = GetFirstArrayItem(
                        searchDocument.RootElement,
                        "results");

                    authorId = firstScholar.HasValue
                        ? GetString(firstScholar.Value, "id")
                        : null;
                    scholarName = firstScholar.HasValue
                        ? GetString(firstScholar.Value, "name")
                        : null;
                    scholarEmail = firstScholar.HasValue
                        ? GetString(firstScholar.Value, "email")
                        : null;
                    scholarPosition = firstScholar.HasValue
                        ? GetString(firstScholar.Value, "position")
                        : null;
                    searchRankingScore = firstScholar.HasValue
                        ? GetDecimal(firstScholar.Value, "ranking_score")
                        : null;
                }

                string? generalRawJson = null;
                string? orcidUrl = null;
                int? publicationCount = null;
                bool? hasVerifiedPublications = null;

                if (!string.IsNullOrWhiteSpace(authorId))
                {
                    scholarsFound++;

                    using var generalDocument =
                        await _client.GetAuthorGeneralAsync(
                            authorId,
                            cancellationToken);

                    generalRawJson = generalDocument.RootElement.GetRawText();
                    scholarName =
                        GetString(generalDocument.RootElement, "name")
                        ?? scholarName;
                    scholarEmail =
                        GetString(generalDocument.RootElement, "email")
                        ?? scholarEmail;
                    scholarPosition =
                        GetString(generalDocument.RootElement, "position")
                        ?? scholarPosition;
                    orcidUrl =
                        GetString(generalDocument.RootElement, "orcid_url");
                    publicationCount =
                        GetInt(generalDocument.RootElement, "publication_count");
                    hasVerifiedPublications =
                        GetBool(
                            generalDocument.RootElement,
                            "has_verified_publications");

                    var allPublications =
                        await _client.GetAllAuthorPublicationsAsync(
                            authorId,
                            verifiedOnly: false,
                            rowsPerPage: rowsPerPage,
                            fromYear: _options.FromYear,
                            toYear: _options.ToYear,
                            cancellationToken: cancellationToken);

                    allPublicationPagesFetched += allPublications.PagesFetched;
                    if (allPublications.HitMaxPages)
                    {
                        paginationSafeguardHitCount++;
                    }

                    publicationRowsInserted +=
                        await SavePublicationSnapshotsAsync(
                            runId,
                            faculty.FacultyId,
                            authorId,
                            isVerifiedQuery: false,
                            allPublications.Publications,
                            cancellationToken);

                    var verifiedPublications =
                        await _client.GetAllAuthorPublicationsAsync(
                            authorId,
                            verifiedOnly: true,
                            rowsPerPage: rowsPerPage,
                            fromYear: _options.FromYear,
                            toYear: _options.ToYear,
                            cancellationToken: cancellationToken);

                    verifiedPublicationPagesFetched +=
                        verifiedPublications.PagesFetched;

                    if (verifiedPublications.HitMaxPages)
                    {
                        paginationSafeguardHitCount++;
                    }

                    verifiedPublicationRowsInserted +=
                        await SavePublicationSnapshotsAsync(
                            runId,
                            faculty.FacultyId,
                            authorId,
                            isVerifiedQuery: true,
                            verifiedPublications.Publications,
                            cancellationToken);
                }
                else
                {
                    facultyWithNoScholar.Add(faculty.FacultyId);
                }

                await _repository.SaveScholarSnapshotAsync(
                    runId,
                    faculty.FacultyId,
                    faculty.DisplayName,
                    faculty.DepartmentName,
                    authorId,
                    scholarName,
                    scholarEmail,
                    scholarPosition,
                    orcidUrl,
                    publicationCount,
                    hasVerifiedPublications,
                    searchRankingScore,
                    searchRawJson,
                    generalRawJson,
                    cancellationToken);
            }

            var summary =
                $"Academic Online comparison completed with full pagination. " +
                $"FacultyProcessed={facultyProcessed}; ScholarsFound={scholarsFound}; " +
                $"PublicationRowsInserted={publicationRowsInserted}; " +
                $"VerifiedPublicationRowsInserted={verifiedPublicationRowsInserted}; " +
                $"AllPublicationPagesFetched={allPublicationPagesFetched}; " +
                $"VerifiedPublicationPagesFetched={verifiedPublicationPagesFetched}; " +
                $"PaginationSafeguardHitCount={paginationSafeguardHitCount}; " +
                $"FacultyWithNoScholar={FormatIdList(facultyWithNoScholar)}.";

            await _repository.CompleteRunAsync(
                runId,
                summary,
                cancellationToken);

            await _logger.LogInfoAsync(
                "AcademicOnlineComparison",
                "RunCompleted",
                summary,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await _repository.FailRunAsync(
                runId,
                ex,
                CancellationToken.None);

            await _logger.LogErrorAsync(
                "AcademicOnlineComparison",
                "RunFailed",
                $"Academic Online comparison run {runId} failed.",
                ex,
                cancellationToken: CancellationToken.None);

            throw;
        }
    }

    private async Task<int> SavePublicationSnapshotsAsync(
        long runId,
        long facultyId,
        string authorId,
        bool isVerifiedQuery,
        IReadOnlyList<AcademicOnlinePublicationItem> publications,
        CancellationToken cancellationToken)
    {
        var inserted = 0;

        foreach (var publication in publications)
        {
            await _repository.SavePublicationSnapshotAsync(
                runId,
                facultyId,
                authorId,
                isVerifiedQuery,
                publication.Id,
                publication.Title,
                publication.Venue,
                publication.YearText,
                publication.PublicationYear,
                publication.CitationCount,
                publication.Doi,
                publication.OpenAlexId,
                publication.Verified,
                publication.Link,
                publication.CoauthorsJson,
                publication.KeywordsJson,
                publication.RawJson,
                cancellationToken);

            inserted++;
        }

        return inserted;
    }

    private static JsonElement? GetFirstArrayItem(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in array.EnumerateArray())
        {
            return item;
        }

        return null;
    }

    private static string? GetString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static int? GetInt(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
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

    private static decimal? GetDecimal(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
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

    private static bool? GetBool(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
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

    private static string FormatIdList(IEnumerable<long> values)
    {
        var ordered = values.OrderBy(value => value).ToList();

        return ordered.Count == 0
            ? "(none)"
            : string.Join(",", ordered);
    }
}
