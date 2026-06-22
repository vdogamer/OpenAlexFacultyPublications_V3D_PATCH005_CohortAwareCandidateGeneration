using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class FacultyIdentityCandidateWorkExpansionJob
{
    private readonly OpenAlexApiClient _client;
    private readonly CandidateGenerationRepository _candidateGenerationRepository;
    private readonly FacultyIdentityCandidateWorkRepository _workRepository;
    private readonly IOperationalLogger _logger;
    private readonly CandidateGenerationOptions _options;

    public FacultyIdentityCandidateWorkExpansionJob(
        OpenAlexApiClient client,
        CandidateGenerationRepository candidateGenerationRepository,
        FacultyIdentityCandidateWorkRepository workRepository,
        IOperationalLogger logger,
        IOptions<CandidateGenerationOptions> options)
    {
        _client = client;
        _candidateGenerationRepository = candidateGenerationRepository;
        _workRepository = workRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var selectedCohortName = NormalizeCohortName(_options.PilotCohortName);
        var institutionFilter = selectedCohortName is null
            ? "FacultyIdentityCandidateWorkExpansion"
            : $"FacultyIdentityCandidateWorkExpansion; PilotCohortName={selectedCohortName}";

        var run = await _candidateGenerationRepository.StartRunAsync(
            _options.RunMode,
            institutionFilter,
            _options.MaxIdentityWorkPagesPerAuthor,
            _options.MaxIdentityWorksPerAuthor,
            _options.ApplicationVersion,
            _options.StartedBy,
            cancellationToken);

        await _logger.LogInfoAsync(
            "FacultyIdentityCandidateWorkExpansion",
            "RunStarted",
            $"Started identity candidate work expansion run {run.IngestionRunId}. PilotCohortName={selectedCohortName ?? "(none)"}.",
            run.RunCorrelationId,
            cancellationToken);

        try
        {
            var faculties = selectedCohortName is null
                ? await _candidateGenerationRepository.LoadFacultyAsync(_options.MaxFacultyToProcess, cancellationToken)
                : await _candidateGenerationRepository.LoadFacultyForPilotCohortAsync(selectedCohortName, _options.MaxFacultyToProcess, cancellationToken);

            var selectedFacultyIds = faculties.Select(f => f.FacultyId).ToList();
            var deletedStaleRows = await _workRepository.DeleteForFacultyIdsAsync(selectedFacultyIds, cancellationToken);

            var candidates = await _workRepository.LoadCandidatesForFacultyIdsAsync(
                selectedFacultyIds,
                _options.MinimumIdentityCandidateEvidenceScoreForWorkExpansion,
                _options.IncludeWeakIdentityCandidatesInWorkExpansion,
                cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyIdentityCandidateWorkExpansion",
                "CandidateSelectionDiagnostics",
                $"SelectedCohortName={selectedCohortName ?? "(none)"}; EnabledCohortFacultyCount={(selectedCohortName is null ? 0 : faculties.Count)}; FacultyIdsSelected={FormatIdList(selectedFacultyIds)}; IdentityCandidatesSelected={candidates.Count}; MinimumEvidenceScore={_options.MinimumIdentityCandidateEvidenceScoreForWorkExpansion}; IncludeWeakIdentityCandidates={_options.IncludeWeakIdentityCandidatesInWorkExpansion}; DeletedStaleWorkRows={deletedStaleRows}.",
                run.RunCorrelationId,
                cancellationToken);

            var inserted = 0;
            var updated = 0;
            var worksSeen = 0;
            var requests = 0;
            var failedCandidateQueries = 0;
            var workCountsByFaculty = selectedFacultyIds.ToDictionary(facultyId => facultyId, _ => 0);

            foreach (var candidate in candidates)
            {
                var authorShortId = candidate.CandidateAuthorOpenAlexShortId ?? GetOpenAlexShortId(candidate.CandidateAuthorOpenAlexId);
                if (string.IsNullOrWhiteSpace(authorShortId))
                {
                    continue;
                }

                var filter = "authorships.author.id:" + authorShortId;
                var cursor = "*";
                var pageNumber = 0;
                var candidateWorksSeen = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (_options.MaxIdentityWorksPerAuthor > 0 && candidateWorksSeen >= _options.MaxIdentityWorksPerAuthor)
                    {
                        break;
                    }

                    if (_options.MaxIdentityWorkPagesPerAuthor > 0 && pageNumber >= _options.MaxIdentityWorkPagesPerAuthor)
                    {
                        break;
                    }

                    pageNumber++;
                    requests++;

                    try
                    {
                        using var page = await _client.GetWorksPageAsync(filter, cursor, cancellationToken);

                        foreach (var workElement in page.ResultsElement.EnumerateArray())
                        {
                            if (_options.MaxIdentityWorksPerAuthor > 0 && candidateWorksSeen >= _options.MaxIdentityWorksPerAuthor)
                            {
                                break;
                            }

                            var work = BuildWorkModel(candidate, authorShortId, workElement, run.IngestionRunId);
                            if (work is null)
                            {
                                continue;
                            }

                            var result = await _workRepository.UpsertAsync(work, cancellationToken);
                            if (result.Inserted) inserted++;
                            if (result.Updated) updated++;

                            worksSeen++;
                            candidateWorksSeen++;
                            workCountsByFaculty[candidate.FacultyId] = workCountsByFaculty.GetValueOrDefault(candidate.FacultyId) + 1;
                        }

                        if (string.IsNullOrWhiteSpace(page.NextCursor) || page.NextCursor == cursor)
                        {
                            break;
                        }

                        cursor = page.NextCursor;
                    }
                    catch (Exception ex)
                    {
                        failedCandidateQueries++;
                        await _logger.LogWarningAsync(
                            "FacultyIdentityCandidateWorkExpansion",
                            "CandidateWorkQueryFailed",
                            $"Work expansion failed for FacultyId={candidate.FacultyId}; FacultyName='{candidate.FacultyDisplayName}'; CandidateAuthor={candidate.CandidateAuthorOpenAlexShortId}; CandidateDisplayName='{candidate.CandidateDisplayName}'. Error={ex.Message}",
                            run.RunCorrelationId,
                            CancellationToken.None);
                        break;
                    }
                }
            }

            var facultyWithZeroExpandedWorks = workCountsByFaculty
                .Where(pair => pair.Value == 0)
                .Select(pair => pair.Key)
                .OrderBy(facultyId => facultyId)
                .ToList();

            var summary =
                $"Faculty identity candidate work expansion completed. SelectedCohortName={selectedCohortName ?? "(none)"}; " +
                $"EnabledCohortFacultyCount={(selectedCohortName is null ? 0 : faculties.Count)}; FacultyIdsSelected={FormatIdList(selectedFacultyIds)}; " +
                $"IdentityCandidatesSelected={candidates.Count}; DeletedStaleWorkRows={deletedStaleRows}; Requests={requests}; FailedCandidateQueries={failedCandidateQueries}; " +
                $"WorksSeen={worksSeen}; WorkRowsInserted={inserted}; WorkRowsUpdated={updated}; WorkCountsByFaculty={FormatCounts(workCountsByFaculty)}; " +
                $"FacultyWithZeroExpandedWorks={FormatIdList(facultyWithZeroExpandedWorks)}.";

            await _candidateGenerationRepository.CompleteRunAsync(
                run.IngestionRunId,
                summary,
                authorsProcessed: candidates.Count,
                worksProcessed: worksSeen,
                cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyIdentityCandidateWorkExpansion",
                "RunCompleted",
                summary,
                run.RunCorrelationId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _candidateGenerationRepository.FailRunAsync(run.IngestionRunId, ex, CancellationToken.None);

            await _logger.LogErrorAsync(
                "FacultyIdentityCandidateWorkExpansion",
                "RunFailed",
                $"Faculty identity candidate work expansion run {run.IngestionRunId} failed.",
                ex,
                run.RunCorrelationId,
                CancellationToken.None);

            throw;
        }
    }

    private static FacultyIdentityCandidateWorkWriteModel? BuildWorkModel(
        FacultyIdentityCandidateForWorkExpansion candidate,
        string authorShortId,
        JsonElement workElement,
        long ingestionRunId)
    {
        var workOpenAlexId = GetString(workElement, "id");
        if (string.IsNullOrWhiteSpace(workOpenAlexId))
        {
            return null;
        }

        var matchedAuthorship = FindMatchedAuthorship(workElement, authorShortId);
        var matchedAuthorDisplayName = GetNestedString(matchedAuthorship, "author", "display_name");
        var matchedAuthorPosition = GetString(matchedAuthorship, "author_position");
        var matchedInstitutionsJson = GetRawJson(matchedAuthorship, "institutions");
        var workDisplayName = GetString(workElement, "display_name") ?? GetString(workElement, "title");
        var workDoi = GetString(workElement, "doi");
        var publicationYear = GetNullableInt32(workElement, "publication_year");
        var publicationDate = GetNullableDate(workElement, "publication_date");
        var workType = GetString(workElement, "type");
        var workShortId = GetOpenAlexShortId(workOpenAlexId);

        return new FacultyIdentityCandidateWorkWriteModel
        {
            FacultyId = candidate.FacultyId,
            CandidateAuthorOpenAlexId = candidate.CandidateAuthorOpenAlexId,
            CandidateAuthorOpenAlexShortId = authorShortId,
            WorkOpenAlexId = workOpenAlexId,
            WorkOpenAlexShortId = workShortId,
            WorkDisplayName = workDisplayName,
            WorkDoi = workDoi,
            PublicationYear = publicationYear,
            PublicationDate = publicationDate,
            WorkType = workType,
            MatchedAuthorDisplayName = matchedAuthorDisplayName,
            MatchedAuthorPosition = matchedAuthorPosition,
            MatchedAuthorInstitutionsJson = matchedInstitutionsJson,
            AuthorshipsJson = GetRawJson(workElement, "authorships"),
            PrimaryLocationJson = GetRawJson(workElement, "primary_location"),
            LocationsJson = GetRawJson(workElement, "locations"),
            WorkRawJson = workElement.GetRawText(),
            EvidenceSummary = $"Expanded work by candidate author ID. Faculty='{candidate.FacultyDisplayName}'; CandidateAuthor={authorShortId}; CandidateDisplayName='{candidate.CandidateDisplayName}'; BestCandidateType={candidate.BestCandidateType}; BestEvidenceScore={candidate.BestEvidenceScore}; Work='{workDisplayName}'; PublicationYear={publicationYear}.",
            IngestionRunId = ingestionRunId
        };
    }

    private static JsonElement FindMatchedAuthorship(JsonElement workElement, string authorShortId)
    {
        if (workElement.ValueKind != JsonValueKind.Object ||
            !workElement.TryGetProperty("authorships", out var authorships) ||
            authorships.ValueKind != JsonValueKind.Array)
        {
            return default;
        }

        foreach (var authorship in authorships.EnumerateArray())
        {
            var currentAuthorId = GetNestedString(authorship, "author", "id");
            if (string.Equals(GetOpenAlexShortId(currentAuthorId), authorShortId, StringComparison.OrdinalIgnoreCase))
            {
                return authorship;
            }
        }

        return default;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? GetNestedString(JsonElement element, string parentPropertyName, string childPropertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(parentPropertyName, out var parent) ||
            parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(parent, childPropertyName);
    }

    private static int? GetNullableInt32(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTime? GetNullableDate(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return DateTime.TryParse(value, out var parsed) ? parsed.Date : null;
    }

    private static string? GetRawJson(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.GetRawText();
    }

    private static string? NormalizeCohortName(string? cohortName)
    {
        return string.IsNullOrWhiteSpace(cohortName) ? null : cohortName.Trim();
    }

    private static string? GetOpenAlexShortId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().TrimEnd('/');
        var slashIndex = trimmed.LastIndexOf('/');
        return slashIndex >= 0 && slashIndex < trimmed.Length - 1
            ? trimmed[(slashIndex + 1)..]
            : trimmed;
    }

    private static string FormatIdList(IEnumerable<long> values)
    {
        var ordered = values.OrderBy(value => value).ToList();
        return ordered.Count == 0 ? "(none)" : string.Join(",", ordered);
    }

    private static string FormatCounts(IReadOnlyDictionary<long, int> counts)
    {
        if (counts.Count == 0)
        {
            return "(none)";
        }

        return string.Join(",", counts
            .OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}:{pair.Value}"));
    }
}
