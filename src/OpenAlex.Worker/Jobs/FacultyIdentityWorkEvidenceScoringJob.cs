using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class FacultyIdentityWorkEvidenceScoringJob
{
    private readonly CandidateGenerationRepository _candidateGenerationRepository;
    private readonly FacultyIdentityWorkEvidenceScoreRepository _scoreRepository;
    private readonly IOperationalLogger _logger;
    private readonly CandidateGenerationOptions _options;

    public FacultyIdentityWorkEvidenceScoringJob(
        CandidateGenerationRepository candidateGenerationRepository,
        FacultyIdentityWorkEvidenceScoreRepository scoreRepository,
        IOperationalLogger logger,
        IOptions<CandidateGenerationOptions> options)
    {
        _candidateGenerationRepository = candidateGenerationRepository;
        _scoreRepository = scoreRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var selectedCohortName = NormalizeCohortName(_options.PilotCohortName);
        var institutionFilter = selectedCohortName is null
            ? "FacultyIdentityWorkEvidenceScoring"
            : $"FacultyIdentityWorkEvidenceScoring; PilotCohortName={selectedCohortName}";

        var run = await _candidateGenerationRepository.StartRunAsync(
            _options.RunMode,
            institutionFilter,
            null,
            _options.MaxPublicationCandidatesToGenerate,
            _options.ApplicationVersion,
            _options.StartedBy,
            cancellationToken);

        await _logger.LogInfoAsync(
            "FacultyIdentityWorkEvidenceScoring",
            "RunStarted",
            $"Started identity work evidence scoring run {run.IngestionRunId}. PilotCohortName={selectedCohortName ?? "(none)"}.",
            run.RunCorrelationId,
            cancellationToken);

        try
        {
            var faculties = selectedCohortName is null
                ? await _candidateGenerationRepository.LoadFacultyAsync(_options.MaxFacultyToProcess, cancellationToken)
                : await _candidateGenerationRepository.LoadFacultyForPilotCohortAsync(selectedCohortName, _options.MaxFacultyToProcess, cancellationToken);

            var selectedFacultyIds = faculties.Select(f => f.FacultyId).ToList();
            var deletedStaleRows = await _scoreRepository.DeleteForFacultyIdsAsync(selectedFacultyIds, cancellationToken);
            var works = await _scoreRepository.LoadWorksForFacultyIdsAsync(selectedFacultyIds, cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyIdentityWorkEvidenceScoring",
                "WorkSelectionDiagnostics",
                $"SelectedCohortName={selectedCohortName ?? "(none)"}; EnabledCohortFacultyCount={(selectedCohortName is null ? 0 : faculties.Count)}; FacultyIdsSelected={FormatIdList(selectedFacultyIds)}; ExpandedWorkRowsSelected={works.Count}; DeletedStaleScoreRows={deletedStaleRows}.",
                run.RunCorrelationId,
                cancellationToken);

            var inserted = 0;
            var updated = 0;
            var scored = 0;
            var statusCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var workCountsByFaculty = selectedFacultyIds.ToDictionary(facultyId => facultyId, _ => 0);

            foreach (var work in works)
            {
                var score = ScoreWork(work, run.IngestionRunId);
                var result = await _scoreRepository.UpsertAsync(score, cancellationToken);

                if (result.Inserted) inserted++;
                if (result.Updated) updated++;

                scored++;
                workCountsByFaculty[work.FacultyId] = workCountsByFaculty.GetValueOrDefault(work.FacultyId) + 1;
                statusCounts[score.WorkEvidenceStatus] = statusCounts.GetValueOrDefault(score.WorkEvidenceStatus) + 1;
            }

            var facultyWithZeroScoredWorks = workCountsByFaculty
                .Where(pair => pair.Value == 0)
                .Select(pair => pair.Key)
                .OrderBy(facultyId => facultyId)
                .ToList();

            var summary =
                $"Faculty identity work evidence scoring completed. SelectedCohortName={selectedCohortName ?? "(none)"}; " +
                $"EnabledCohortFacultyCount={(selectedCohortName is null ? 0 : faculties.Count)}; FacultyIdsSelected={FormatIdList(selectedFacultyIds)}; " +
                $"ExpandedWorkRowsSelected={works.Count}; DeletedStaleScoreRows={deletedStaleRows}; WorksScored={scored}; " +
                $"ScoreRowsInserted={inserted}; ScoreRowsUpdated={updated}; StatusCounts={FormatCounts(statusCounts)}; " +
                $"WorkCountsByFaculty={FormatCounts(workCountsByFaculty)}; FacultyWithZeroScoredWorks={FormatIdList(facultyWithZeroScoredWorks)}.";

            await _candidateGenerationRepository.CompleteRunAsync(
                run.IngestionRunId,
                summary,
                authorsProcessed: faculties.Count,
                worksProcessed: scored,
                cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyIdentityWorkEvidenceScoring",
                "RunCompleted",
                summary,
                run.RunCorrelationId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _candidateGenerationRepository.FailRunAsync(run.IngestionRunId, ex, CancellationToken.None);

            await _logger.LogErrorAsync(
                "FacultyIdentityWorkEvidenceScoring",
                "RunFailed",
                $"Faculty identity work evidence scoring run {run.IngestionRunId} failed.",
                ex,
                run.RunCorrelationId,
                CancellationToken.None);

            throw;
        }
    }

    private static FacultyIdentityWorkEvidenceScoreWriteModel ScoreWork(FacultyIdentityWorkForScoring work, long ingestionRunId)
    {
        var nameScore = ScoreName(work);
        var institutionScore = ScoreInstitution(work.MatchedAuthorInstitutionsJson);
        var doiScore = string.IsNullOrWhiteSpace(work.WorkDoi) ? 0m : 5m;
        var publicationYearScore = work.PublicationYear.HasValue ? 5m : 0m;
        var overall = Math.Min(100m, Math.Round((nameScore * 0.75m) + (institutionScore * 0.15m) + doiScore + publicationYearScore, 4));
        var status = GetStatus(overall, nameScore);

        var evidence = new
        {
            work.FacultyId,
            FacultyDisplayName = work.FacultyDisplayName,
            work.CandidateAuthorOpenAlexId,
            work.CandidateAuthorOpenAlexShortId,
            work.WorkOpenAlexId,
            work.WorkOpenAlexShortId,
            work.MatchedAuthorDisplayName,
            work.WorkDisplayName,
            work.PublicationYear,
            Scores = new
            {
                NameEvidenceScore = nameScore,
                InstitutionEvidenceScore = institutionScore,
                DoiEvidenceScore = doiScore,
                PublicationYearEvidenceScore = publicationYearScore,
                OverallWorkEvidenceScore = overall
            },
            MatchedAuthorInstitutionsJson = work.MatchedAuthorInstitutionsJson,
            SourceEvidenceSummary = work.EvidenceSummary
        };

        return new FacultyIdentityWorkEvidenceScoreWriteModel
        {
            FacultyId = work.FacultyId,
            CandidateAuthorOpenAlexId = work.CandidateAuthorOpenAlexId,
            CandidateAuthorOpenAlexShortId = work.CandidateAuthorOpenAlexShortId,
            WorkOpenAlexId = work.WorkOpenAlexId,
            WorkOpenAlexShortId = work.WorkOpenAlexShortId,
            MatchedAuthorDisplayName = work.MatchedAuthorDisplayName,
            WorkDisplayName = work.WorkDisplayName,
            PublicationYear = work.PublicationYear,
            NameEvidenceScore = nameScore,
            InstitutionEvidenceScore = institutionScore,
            DoiEvidenceScore = doiScore,
            PublicationYearEvidenceScore = publicationYearScore,
            OverallWorkEvidenceScore = overall,
            WorkEvidenceStatus = status,
            EvidenceSummary = $"WorkEvidenceStatus={status}; Faculty='{work.FacultyDisplayName}'; MatchedAuthor='{work.MatchedAuthorDisplayName}'; CandidateAuthor={work.CandidateAuthorOpenAlexShortId}; Work='{work.WorkDisplayName}'; PublicationYear={work.PublicationYear}; OverallScore={overall}; NameScore={nameScore}; InstitutionScore={institutionScore}; DoiScore={doiScore}; PublicationYearScore={publicationYearScore}.",
            EvidenceJson = JsonSerializer.Serialize(evidence),
            IngestionRunId = ingestionRunId
        };
    }

    private static decimal ScoreName(FacultyIdentityWorkForScoring work)
    {
        var facultyFull = NormalizeName(work.FacultyDisplayName);
        var facultyFirst = NormalizeName(work.FacultyFirstName);
        var facultyLast = NormalizeName(work.FacultyLastName);
        var matchedAuthor = NormalizeName(work.MatchedAuthorDisplayName);

        if (string.IsNullOrWhiteSpace(facultyFirst) || string.IsNullOrWhiteSpace(facultyLast))
        {
            var parsed = ParseName(work.FacultyDisplayName);
            facultyFirst = string.IsNullOrWhiteSpace(facultyFirst) ? parsed.First ?? string.Empty : facultyFirst;
            facultyLast = string.IsNullOrWhiteSpace(facultyLast) ? parsed.Last ?? string.Empty : facultyLast;
        }

        if (!string.IsNullOrWhiteSpace(facultyFull) && facultyFull == matchedAuthor)
        {
            return 100m;
        }

        var matchedParsed = ParseName(work.MatchedAuthorDisplayName);
        if (!string.IsNullOrWhiteSpace(facultyFirst) &&
            !string.IsNullOrWhiteSpace(facultyLast) &&
            facultyFirst == matchedParsed.First &&
            facultyLast == matchedParsed.Last)
        {
            return 95m;
        }

        if (!string.IsNullOrWhiteSpace(facultyFirst) &&
            !string.IsNullOrWhiteSpace(facultyLast) &&
            !string.IsNullOrWhiteSpace(matchedParsed.First) &&
            matchedParsed.First[0] == facultyFirst[0] &&
            matchedParsed.Last == facultyLast)
        {
            return 82m;
        }

        if (!string.IsNullOrWhiteSpace(facultyFirst) &&
            !string.IsNullOrWhiteSpace(facultyLast) &&
            matchedAuthor.Contains(facultyFirst) &&
            matchedAuthor.Contains(facultyLast))
        {
            return 70m;
        }

        if (!string.IsNullOrWhiteSpace(facultyLast) && matchedParsed.Last == facultyLast)
        {
            return 55m;
        }

        return 0m;
    }

    private static decimal ScoreInstitution(string? matchedAuthorInstitutionsJson)
    {
        if (string.IsNullOrWhiteSpace(matchedAuthorInstitutionsJson))
        {
            return 0m;
        }

        if (matchedAuthorInstitutionsJson.Contains("University of Virginia", StringComparison.OrdinalIgnoreCase))
        {
            return 100m;
        }

        if (matchedAuthorInstitutionsJson.Contains("Virginia", StringComparison.OrdinalIgnoreCase))
        {
            return 40m;
        }

        return 10m;
    }

    private static string GetStatus(decimal overallScore, decimal nameScore)
    {
        if (overallScore >= 80m && nameScore >= 82m)
        {
            return "StrongWorkEvidence";
        }

        if (overallScore >= 65m && nameScore >= 55m)
        {
            return "ModerateWorkEvidence";
        }

        if (overallScore >= 45m && nameScore >= 55m)
        {
            return "WeakWorkEvidence";
        }

        if (nameScore <= 0m)
        {
            return "ConflictingWorkEvidence";
        }

        return "NeedsManualReview";
    }

    private static string? NormalizeCohortName(string? cohortName)
    {
        return string.IsNullOrWhiteSpace(cohortName) ? null : cohortName.Trim();
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

    private static (string? First, string? Last) ParseName(string? value)
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

    private static string FormatIdList(IEnumerable<long> values)
    {
        var ordered = values.OrderBy(value => value).ToList();
        return ordered.Count == 0 ? "(none)" : string.Join(",", ordered);
    }

    private static string FormatCounts<TKey>(IReadOnlyDictionary<TKey, int> counts) where TKey : notnull
    {
        if (counts.Count == 0)
        {
            return "(none)";
        }

        return string.Join(",", counts
            .OrderBy(pair => pair.Key?.ToString())
            .Select(pair => $"{pair.Key}:{pair.Value}"));
    }
}
