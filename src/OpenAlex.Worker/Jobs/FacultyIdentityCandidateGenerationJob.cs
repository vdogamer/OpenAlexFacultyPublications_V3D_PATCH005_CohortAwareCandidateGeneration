using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class FacultyIdentityCandidateGenerationJob
{
    private readonly OpenAlexApiClient _client;
    private readonly CandidateGenerationRepository _candidateGenerationRepository;
    private readonly FacultyIdentityCandidateRepository _identityCandidateRepository;
    private readonly IOperationalLogger _logger;
    private readonly CandidateGenerationOptions _options;

    public FacultyIdentityCandidateGenerationJob(
        OpenAlexApiClient client,
        CandidateGenerationRepository candidateGenerationRepository,
        FacultyIdentityCandidateRepository identityCandidateRepository,
        IOperationalLogger logger,
        IOptions<CandidateGenerationOptions> options)
    {
        _client = client;
        _candidateGenerationRepository = candidateGenerationRepository;
        _identityCandidateRepository = identityCandidateRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var selectedCohortName = NormalizeCohortName(_options.PilotCohortName);
        var institutionFilter = selectedCohortName is null
            ? "FacultyIdentityCandidateGeneration"
            : $"FacultyIdentityCandidateGeneration; PilotCohortName={selectedCohortName}";

        var run = await _candidateGenerationRepository.StartRunAsync(
            _options.RunMode,
            institutionFilter,
            null,
            _options.MaxIdentityAuthorSearchResults,
            _options.ApplicationVersion,
            _options.StartedBy,
            cancellationToken);

        await _logger.LogInfoAsync(
            "FacultyIdentityCandidateGeneration",
            "RunStarted",
            $"Started faculty identity candidate generation run {run.IngestionRunId}. PilotCohortName={selectedCohortName ?? "(none)"}.",
            run.RunCorrelationId,
            cancellationToken);

        try
        {
            var faculties = selectedCohortName is null
                ? await _candidateGenerationRepository.LoadFacultyAsync(_options.MaxFacultyToProcess, cancellationToken)
                : await _candidateGenerationRepository.LoadFacultyForPilotCohortAsync(selectedCohortName, _options.MaxFacultyToProcess, cancellationToken);

            var selectedFacultyIds = faculties.Select(f => f.FacultyId).ToList();

            await _logger.LogInfoAsync(
                "FacultyIdentityCandidateGeneration",
                "CohortSelectionDiagnostics",
                $"SelectedCohortName={selectedCohortName ?? "(none)"}; EnabledCohortFacultyCount={(selectedCohortName is null ? 0 : faculties.Count)}; FacultyIdsSelected={FormatIdList(selectedFacultyIds)}; MaxFacultyToProcess={_options.MaxFacultyToProcess}.",
                run.RunCorrelationId,
                cancellationToken);

            var inserted = 0;
            var updated = 0;
            var facultyProcessed = 0;
            var candidateCountsByFaculty = faculties.ToDictionary(f => f.FacultyId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            foreach (var faculty in faculties)
            {
                facultyProcessed++;

                var discoveryQueries = BuildDiscoveryQueries(faculty);
                foreach (var discoveryQuery in discoveryQueries)
                {
                    if (discoveryQuery.IsClaimedAuthorId)
                    {
                        var candidate = BuildClaimedAuthorCandidate(faculty, discoveryQuery, run.IngestionRunId);
                        var result = await _identityCandidateRepository.UpsertAsync(candidate, cancellationToken);
                        if (result.Inserted) inserted++;
                        if (result.Updated) updated++;
                        candidateCountsByFaculty[faculty.FacultyId].Add(candidate.CandidateAuthorOpenAlexId);
                        continue;
                    }

                    try
                    {
                        using var page = discoveryQuery.IsFilter
                            ? await _client.GetAuthorsFilterPageAsync(discoveryQuery.Value, _options.MaxIdentityAuthorSearchResults, cancellationToken)
                            : await _client.GetAuthorsSearchPageAsync(discoveryQuery.Value, _options.MaxIdentityAuthorSearchResults, cancellationToken);

                        foreach (var item in page.ResultsElement.EnumerateArray())
                        {
                            var candidate = BuildOpenAlexAuthorCandidate(
                                faculty,
                                discoveryQuery,
                                item,
                                run.IngestionRunId);

                            if (candidate is null)
                            {
                                continue;
                            }

                            var result = await _identityCandidateRepository.UpsertAsync(candidate, cancellationToken);
                            if (result.Inserted) inserted++;
                            if (result.Updated) updated++;
                            candidateCountsByFaculty[faculty.FacultyId].Add(candidate.CandidateAuthorOpenAlexId);
                        }
                    }
                    catch (Exception ex)
                    {
                        await _logger.LogWarningAsync(
                            "FacultyIdentityCandidateGeneration",
                            "DiscoveryQueryFailed",
                            $"Discovery query failed for FacultyId={faculty.FacultyId}; FacultyName='{faculty.DisplayName}'; DiscoveryMethod={discoveryQuery.DiscoveryMethod}; QueryValue='{discoveryQuery.Value}'. Error={ex.Message}",
                            run.RunCorrelationId,
                            CancellationToken.None);
                    }
                }
            }

            var facultyWithZeroIdentityCandidates = candidateCountsByFaculty
                .Where(pair => pair.Value.Count == 0)
                .Select(pair => pair.Key)
                .OrderBy(facultyId => facultyId)
                .ToList();

            var summary =
                $"Faculty identity candidate generation completed. SelectedCohortName={selectedCohortName ?? "(none)"}; " +
                $"EnabledCohortFacultyCount={(selectedCohortName is null ? 0 : faculties.Count)}; FacultyIdsSelected={FormatIdList(selectedFacultyIds)}; " +
                $"FacultyProcessed={facultyProcessed}; IdentityCandidatesInserted={inserted}; IdentityCandidatesUpdated={updated}; " +
                $"CandidateCountsByFaculty={FormatCandidateCounts(candidateCountsByFaculty)}; " +
                $"FacultyWithZeroIdentityCandidates={FormatIdList(facultyWithZeroIdentityCandidates)}.";

            await _candidateGenerationRepository.CompleteRunAsync(
                run.IngestionRunId,
                summary,
                authorsProcessed: facultyProcessed,
                worksProcessed: 0,
                cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyIdentityCandidateGeneration",
                "RunCompleted",
                summary,
                run.RunCorrelationId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _candidateGenerationRepository.FailRunAsync(run.IngestionRunId, ex, CancellationToken.None);

            await _logger.LogErrorAsync(
                "FacultyIdentityCandidateGeneration",
                "RunFailed",
                $"Faculty identity candidate generation run {run.IngestionRunId} failed.",
                ex,
                run.RunCorrelationId,
                CancellationToken.None);

            throw;
        }
    }

    private static IReadOnlyList<IdentityDiscoveryQuery> BuildDiscoveryQueries(FacultyCandidateSource faculty)
    {
        var queries = new List<IdentityDiscoveryQuery>();
        var fullName = NormalizeWhitespace(faculty.DisplayName);
        var firstName = NormalizeWhitespace(faculty.FirstName);
        var lastName = NormalizeWhitespace(faculty.LastName);

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            var parsed = ParseFacultyName(faculty.DisplayName);
            firstName = string.IsNullOrWhiteSpace(firstName) ? parsed.First ?? string.Empty : firstName;
            lastName = string.IsNullOrWhiteSpace(lastName) ? parsed.Last ?? string.Empty : lastName;
        }

        if (!string.IsNullOrWhiteSpace(faculty.ClaimedOpenAlexAuthorId))
        {
            queries.Add(new IdentityDiscoveryQuery
            {
                DiscoveryMethod = "ClaimedOpenAlexAuthorId",
                Value = NormalizeOpenAlexId(faculty.ClaimedOpenAlexAuthorId) ?? faculty.ClaimedOpenAlexAuthorId.Trim(),
                IsClaimedAuthorId = true,
                BaseEvidenceScore = 95m
            });
        }

        if (!string.IsNullOrWhiteSpace(faculty.Orcid))
        {
            queries.Add(new IdentityDiscoveryQuery
            {
                DiscoveryMethod = "OrcidLookup",
                Value = "orcid:" + NormalizeOrcid(faculty.Orcid),
                IsFilter = true,
                BaseEvidenceScore = 92m
            });
        }

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            queries.Add(new IdentityDiscoveryQuery
            {
                DiscoveryMethod = "ExactFullNameSearch",
                Value = fullName,
                BaseEvidenceScore = 70m
            });
        }

        if (!string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName))
        {
            queries.Add(new IdentityDiscoveryQuery
            {
                DiscoveryMethod = "FirstLastNameSearch",
                Value = firstName + " " + lastName,
                BaseEvidenceScore = 62m
            });

            var firstInitial = firstName.Substring(0, 1);
            queries.Add(new IdentityDiscoveryQuery
            {
                DiscoveryMethod = "FirstInitialLastNameSearch",
                Value = firstInitial + " " + lastName,
                BaseEvidenceScore = 48m
            });
        }

        var firstMiddleLast = BuildFirstMiddleLastName(faculty.DisplayName, firstName, lastName);
        if (!string.IsNullOrWhiteSpace(firstMiddleLast) &&
            !queries.Any(q => string.Equals(q.Value, firstMiddleLast, StringComparison.OrdinalIgnoreCase)))
        {
            queries.Add(new IdentityDiscoveryQuery
            {
                DiscoveryMethod = "FirstMiddleLastNameSearch",
                Value = firstMiddleLast,
                BaseEvidenceScore = 66m
            });
        }

        return queries
            .Where(q => !string.IsNullOrWhiteSpace(q.Value))
            .GroupBy(q => q.DiscoveryMethod + "|" + q.Value, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static FacultyIdentityCandidateWriteModel BuildClaimedAuthorCandidate(
        FacultyCandidateSource faculty,
        IdentityDiscoveryQuery discoveryQuery,
        long ingestionRunId)
    {
        var normalizedAuthorId = NormalizeOpenAlexId(discoveryQuery.Value) ?? discoveryQuery.Value;
        var shortId = GetOpenAlexShortId(normalizedAuthorId);
        var evidenceScore = discoveryQuery.BaseEvidenceScore;
        var evidenceStatus = GetEvidenceStatus(evidenceScore, discoveryQuery.DiscoveryMethod);

        var evidence = new
        {
            faculty.FacultyId,
            FacultyDisplayName = faculty.DisplayName,
            discoveryQuery.DiscoveryMethod,
            CandidateAuthorOpenAlexId = normalizedAuthorId,
            CandidateAuthorOpenAlexShortId = shortId,
            Note = "Claimed/manual author ID is treated as a strong candidate for review, not an automatic acceptance."
        };

        return new FacultyIdentityCandidateWriteModel
        {
            FacultyId = faculty.FacultyId,
            CandidateAuthorOpenAlexId = normalizedAuthorId,
            CandidateAuthorOpenAlexShortId = shortId,
            CandidateDisplayName = faculty.DisplayName,
            CandidateOrcid = faculty.Orcid,
            DiscoveryMethod = discoveryQuery.DiscoveryMethod,
            EvidenceScore = evidenceScore,
            EvidenceStatus = evidenceStatus,
            EvidenceSummary = $"{discoveryQuery.DiscoveryMethod}: Faculty '{faculty.DisplayName}' has claimed/manual OpenAlex author ID '{normalizedAuthorId}'. EvidenceScore={evidenceScore}. Candidate requires review.",
            EvidenceJson = JsonSerializer.Serialize(evidence),
            IngestionRunId = ingestionRunId
        };
    }

    private static FacultyIdentityCandidateWriteModel? BuildOpenAlexAuthorCandidate(
        FacultyCandidateSource faculty,
        IdentityDiscoveryQuery discoveryQuery,
        JsonElement author,
        long ingestionRunId)
    {
        var authorId = GetString(author, "id");
        if (string.IsNullOrWhiteSpace(authorId))
        {
            return null;
        }

        var displayName = GetString(author, "display_name");
        var orcid = GetString(author, "orcid");
        var worksCount = GetNullableInt32(author, "works_count");
        var citedByCount = GetNullableInt32(author, "cited_by_count");
        var shortId = GetOpenAlexShortId(authorId);
        var lastKnownInstitutionsJson = GetRawJson(author, "last_known_institutions");
        var institutionSummary = BuildInstitutionSummary(author, "last_known_institutions");
        var nameScore = ScoreName(faculty, displayName);
        var institutionScore = institutionSummary.Contains("University of Virginia", StringComparison.OrdinalIgnoreCase) ? 10m : 0m;
        var identifierBonus = discoveryQuery.DiscoveryMethod is "OrcidLookup" ? 8m : 0m;
        var worksBonus = worksCount.GetValueOrDefault() > 0 ? 2m : 0m;
        var evidenceScore = Math.Min(100m, discoveryQuery.BaseEvidenceScore + (nameScore * 0.15m) + institutionScore + identifierBonus + worksBonus);
        var evidenceStatus = GetEvidenceStatus(evidenceScore, discoveryQuery.DiscoveryMethod);

        var evidence = new
        {
            faculty.FacultyId,
            FacultyDisplayName = faculty.DisplayName,
            discoveryQuery.DiscoveryMethod,
            QueryValue = discoveryQuery.Value,
            CandidateAuthorOpenAlexId = authorId,
            CandidateAuthorOpenAlexShortId = shortId,
            CandidateDisplayName = displayName,
            CandidateOrcid = orcid,
            WorksCount = worksCount,
            CitedByCount = citedByCount,
            LastKnownInstitutions = institutionSummary,
            Scores = new
            {
                BaseEvidenceScore = discoveryQuery.BaseEvidenceScore,
                NameScore = nameScore,
                InstitutionBonus = institutionScore,
                IdentifierBonus = identifierBonus,
                WorksBonus = worksBonus,
                EvidenceScore = evidenceScore
            },
            OpenAlexAuthorRawJson = author.GetRawText()
        };

        return new FacultyIdentityCandidateWriteModel
        {
            FacultyId = faculty.FacultyId,
            CandidateAuthorOpenAlexId = authorId,
            CandidateAuthorOpenAlexShortId = shortId,
            CandidateDisplayName = displayName,
            CandidateOrcid = orcid,
            WorksCount = worksCount,
            CitedByCount = citedByCount,
            LastKnownInstitutionsJson = lastKnownInstitutionsJson,
            DiscoveryMethod = discoveryQuery.DiscoveryMethod,
            EvidenceScore = Math.Round(evidenceScore, 4),
            EvidenceStatus = evidenceStatus,
            EvidenceSummary = $"{discoveryQuery.DiscoveryMethod}: Faculty '{faculty.DisplayName}' discovered OpenAlex author '{displayName}' ({shortId}). EvidenceScore={Math.Round(evidenceScore, 4)}; NameScore={nameScore}; LastKnownInstitutions='{institutionSummary}'. Candidate requires review.",
            EvidenceJson = JsonSerializer.Serialize(evidence),
            IngestionRunId = ingestionRunId
        };
    }

    private static string GetEvidenceStatus(decimal evidenceScore, string discoveryMethod)
    {
        if ((discoveryMethod is "ClaimedOpenAlexAuthorId" or "OrcidLookup") && evidenceScore >= 90m)
        {
            return "StrongIdentifierCandidate";
        }

        if (evidenceScore >= 70m)
        {
            return "CandidateForReview";
        }

        if (evidenceScore >= 55m)
        {
            return "PossibleNameCandidate";
        }

        if (evidenceScore > 0m)
        {
            return "WeakNameCandidate";
        }

        return "NeedsMoreEvidence";
    }

    private static decimal ScoreName(FacultyCandidateSource faculty, string? authorDisplayName)
    {
        if (string.IsNullOrWhiteSpace(faculty.DisplayName) || string.IsNullOrWhiteSpace(authorDisplayName))
        {
            return 0m;
        }

        var facultyParsed = ParseFacultyName(faculty.DisplayName);
        var authorParsed = ParseFacultyName(authorDisplayName);
        var facultyFull = NormalizeName(faculty.DisplayName);
        var authorFull = NormalizeName(authorDisplayName);

        if (!string.IsNullOrWhiteSpace(facultyFull) && facultyFull == authorFull)
        {
            return 100m;
        }

        if (!string.IsNullOrWhiteSpace(faculty.FirstName) && !string.IsNullOrWhiteSpace(faculty.LastName))
        {
            facultyParsed = (NormalizeName(faculty.FirstName), NormalizeName(faculty.LastName));
        }

        if (!string.IsNullOrWhiteSpace(facultyParsed.First) &&
            !string.IsNullOrWhiteSpace(facultyParsed.Last) &&
            facultyParsed.First == authorParsed.First &&
            facultyParsed.Last == authorParsed.Last)
        {
            return 95m;
        }

        if (!string.IsNullOrWhiteSpace(facultyParsed.First) &&
            !string.IsNullOrWhiteSpace(authorParsed.First) &&
            !string.IsNullOrWhiteSpace(facultyParsed.Last) &&
            facultyParsed.Last == authorParsed.Last &&
            facultyParsed.First[0] == authorParsed.First[0])
        {
            return 82m;
        }

        if (!string.IsNullOrWhiteSpace(facultyParsed.Last) && facultyParsed.Last == authorParsed.Last)
        {
            return 55m;
        }

        return 0m;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
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

    private static string? GetRawJson(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.GetRawText();
    }

    private static string BuildInstitutionSummary(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var institutions) || institutions.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var names = new List<string>();
        foreach (var institution in institutions.EnumerateArray())
        {
            var name = GetString(institution, "display_name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return string.Join(" | ", names.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string? NormalizeCohortName(string? cohortName)
    {
        return string.IsNullOrWhiteSpace(cohortName) ? null : cohortName.Trim();
    }

    private static string NormalizeWhitespace(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            .ToArray())
            .Trim();
    }

    private static (string? First, string? Last) ParseFacultyName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var normalized = value.Trim();
        if (normalized.Contains(','))
        {
            var parts = normalized.Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                var first = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return (NormalizeName(first), NormalizeName(parts[0]));
            }
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1)
        {
            return (NormalizeName(tokens[0]), null);
        }

        return (NormalizeName(tokens[0]), NormalizeName(tokens[^1]));
    }

    private static string? BuildFirstMiddleLastName(string? displayName, string? firstName, string? lastName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            return null;
        }

        var normalized = displayName.Trim();
        if (normalized.Contains(','))
        {
            return null;
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3)
        {
            return null;
        }

        return string.Join(" ", tokens);
    }

    private static string NormalizeOrcid(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("https://orcid.org/", "", StringComparison.OrdinalIgnoreCase).Trim();
    }

    private static string? NormalizeOpenAlexId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().TrimEnd('/');
        if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.Length > 1 && char.IsLetter(trimmed[0]))
        {
            return "https://openalex.org/" + trimmed.ToUpperInvariant();
        }

        return trimmed;
    }

    private static string? GetOpenAlexShortId(string? value)
    {
        var normalized = NormalizeOpenAlexId(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var slashIndex = normalized.LastIndexOf('/');
        return slashIndex >= 0 && slashIndex < normalized.Length - 1
            ? normalized[(slashIndex + 1)..]
            : normalized;
    }

    private static string FormatIdList(IEnumerable<long> values)
    {
        var ordered = values.OrderBy(value => value).ToList();
        return ordered.Count == 0 ? "(none)" : string.Join(",", ordered);
    }

    private static string FormatCandidateCounts(IReadOnlyDictionary<long, HashSet<string>> countsByFaculty)
    {
        if (countsByFaculty.Count == 0)
        {
            return "(none)";
        }

        return string.Join(",", countsByFaculty
            .OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}:{pair.Value.Count}"));
    }

    private sealed class IdentityDiscoveryQuery
    {
        public string DiscoveryMethod { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool IsFilter { get; set; }
        public bool IsClaimedAuthorId { get; set; }
        public decimal BaseEvidenceScore { get; set; }
    }
}
