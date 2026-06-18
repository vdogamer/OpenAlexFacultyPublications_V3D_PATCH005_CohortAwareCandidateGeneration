using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class FacultyAuthorCandidateGenerationJob
{
    private readonly CandidateGenerationRepository _repository;
    private readonly IOperationalLogger _logger;
    private readonly CandidateGenerationOptions _options;
    private readonly OpenAlexOptions _openAlexOptions;

    public FacultyAuthorCandidateGenerationJob(
        CandidateGenerationRepository repository,
        IOperationalLogger logger,
        IOptions<CandidateGenerationOptions> options,
        IOptions<OpenAlexOptions> openAlexOptions)
    {
        _repository = repository;
        _logger = logger;
        _options = options.Value;
        _openAlexOptions = openAlexOptions.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var institutionFilter = string.IsNullOrWhiteSpace(_options.PilotCohortName)
            ? "FacultyAuthorCandidateGeneration"
            : $"FacultyAuthorCandidateGeneration; PilotCohortName={_options.PilotCohortName}";

        var run = await _repository.StartRunAsync(
            _options.RunMode,
            institutionFilter,
            null,
            _options.MaxAuthorsToScan,
            _options.ApplicationVersion,
            _options.StartedBy,
            cancellationToken);

        await _logger.LogInfoAsync(
            "FacultyAuthorCandidateGeneration",
            "RunStarted",
            $"Started faculty-author candidate generation run {run.IngestionRunId}. PilotCohortName={_options.PilotCohortName ?? "(none)"}.",
            run.RunCorrelationId,
            cancellationToken);

        try
        {
            var faculties = string.IsNullOrWhiteSpace(_options.PilotCohortName)
                ? await _repository.LoadFacultyAsync(_options.MaxFacultyToProcess, cancellationToken)
                : await _repository.LoadFacultyForPilotCohortAsync(_options.PilotCohortName, _options.MaxFacultyToProcess, cancellationToken);
            var authors = await _repository.LoadAuthorsAsync(_options.MaxAuthorsToScan, cancellationToken);
            var institutions = await _repository.LoadAuthorInstitutionsAsync(authors.Select(a => a.AuthorId).ToList(), cancellationToken);

            var configuredInstitutionIds = _openAlexOptions.InstitutionIds
                .SelectMany(id => new[] { NormalizeOpenAlexId(id), GetOpenAlexShortId(id) })
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var inserted = 0;
            var updated = 0;

            foreach (var faculty in faculties)
            {
                var candidateRows = new List<AuthorCandidateWriteModel>();

                foreach (var author in authors)
                {
                    var authorInstitutions = institutions.TryGetValue(author.AuthorId, out var list)
                        ? list
                        : new List<AuthorInstitutionEvidence>();

                    var candidate = ScoreCandidate(
                        faculty,
                        author,
                        authorInstitutions,
                        configuredInstitutionIds,
                        run.IngestionRunId);

                    if (candidate is not null)
                    {
                        candidateRows.Add(candidate);
                    }
                }

                foreach (var candidate in candidateRows
                    .OrderByDescending(c => c.MatchScore)
                    .ThenByDescending(c => c.NameScore)
                    .Take(_options.MaxCandidatesPerFaculty))
                {
                    var wasInserted = await _repository.UpsertAuthorCandidateAsync(candidate, cancellationToken);
                    if (wasInserted) inserted++; else updated++;
                }
            }

            var summary =
                $"Faculty-author candidate generation completed. FacultyProcessed={faculties.Count}; " +
                $"AuthorsScanned={authors.Count}; CandidatesInserted={inserted}; CandidatesUpdated={updated}.";

            await _repository.CompleteRunAsync(
                run.IngestionRunId,
                summary,
                authorsProcessed: authors.Count,
                worksProcessed: 0,
                cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyAuthorCandidateGeneration",
                "RunCompleted",
                summary,
                run.RunCorrelationId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _repository.FailRunAsync(run.IngestionRunId, ex, CancellationToken.None);

            await _logger.LogErrorAsync(
                "FacultyAuthorCandidateGeneration",
                "RunFailed",
                $"Faculty-author candidate generation run {run.IngestionRunId} failed.",
                ex,
                run.RunCorrelationId,
                CancellationToken.None);

            throw;
        }
    }

    private AuthorCandidateWriteModel? ScoreCandidate(
        FacultyCandidateSource faculty,
        AuthorCandidateSource author,
        List<AuthorInstitutionEvidence> institutions,
        HashSet<string> configuredInstitutionIds,
        long runId)
    {
        var nameScore = ScoreName(faculty, author.DisplayName);
        var orcidScore = OrcidEquals(faculty.Orcid, author.Orcid) ? 100m : 0m;
        var claimedScore = OpenAlexIdEquals(faculty.ClaimedOpenAlexAuthorId, author.OpenAlexId) ? 100m : 0m;
        var institutionScore = ScoreInstitution(institutions, configuredInstitutionIds);

        string candidateSource;
        decimal matchScore;

        if (claimedScore == 100m)
        {
            candidateSource = "ClaimedOpenAlexAuthorId";
            matchScore = 100m;
        }
        else if (orcidScore == 100m)
        {
            candidateSource = "ExactOrcid";
            matchScore = 98m;
        }
        else if (nameScore >= _options.MinimumNameScore && institutionScore > 0)
        {
            candidateSource = "NameAndInstitutionSearch";
            matchScore = Math.Round((nameScore * 0.75m) + (institutionScore * 0.25m), 4);
        }
        else if (nameScore >= 70m)
        {
            candidateSource = "NameSearch";
            matchScore = Math.Round(nameScore * 0.70m, 4);
        }
        else
        {
            return null;
        }

        if (matchScore < _options.MinimumMatchScore)
        {
            return null;
        }

        var status = candidateSource is "ClaimedOpenAlexAuthorId" or "ExactOrcid"
            ? "AutoAcceptedCandidate"
            : matchScore >= 80m ? "PendingReview" : "NeedsMoreEvidence";

        var evidence = new
        {
            faculty,
            author,
            scores = new
            {
                NameScore = nameScore,
                OrcidScore = orcidScore,
                ClaimedOpenAlexScore = claimedScore,
                InstitutionScore = institutionScore,
                MatchScore = matchScore
            },
            institutions
        };

        return new AuthorCandidateWriteModel
        {
            FacultyId = faculty.FacultyId,
            AuthorId = author.AuthorId,
            AuthorOpenAlexId = author.OpenAlexId,
            AuthorOpenAlexShortId = author.OpenAlexShortId,
            AuthorDisplayName = author.DisplayName,
            AuthorOrcid = author.Orcid,
            CandidateSource = candidateSource,
            CandidateStatus = status,
            MatchScore = matchScore,
            NameScore = nameScore,
            OrcidScore = orcidScore,
            InstitutionScore = institutionScore,
            CoauthorScore = 0,
            PublicationScore = 0,
            EvidenceSummary = $"{candidateSource}: Faculty '{faculty.DisplayName}' compared with OpenAlex author '{author.DisplayName}'. MatchScore={matchScore}; NameScore={nameScore}; OrcidScore={orcidScore}; ClaimedOpenAlexScore={claimedScore}; InstitutionScore={institutionScore}.",
            EvidenceJson = JsonSerializer.Serialize(evidence),
            IngestionRunId = runId
        };
    }

    private static decimal ScoreInstitution(List<AuthorInstitutionEvidence> institutions, HashSet<string> configuredInstitutionIds)
    {
        if (institutions.Count == 0)
        {
            return 0m;
        }

        foreach (var institution in institutions)
        {
            var full = NormalizeOpenAlexId(institution.InstitutionOpenAlexId);
            var shortId = GetOpenAlexShortId(institution.InstitutionOpenAlexId);

            if ((!string.IsNullOrWhiteSpace(full) && configuredInstitutionIds.Contains(full)) ||
                (!string.IsNullOrWhiteSpace(shortId) && configuredInstitutionIds.Contains(shortId)))
            {
                return 100m;
            }
        }

        if (institutions.Any(i => (i.InstitutionDisplayName ?? string.Empty).Contains("University of Virginia", StringComparison.OrdinalIgnoreCase)))
        {
            return 90m;
        }

        return 25m;
    }

    private static decimal ScoreName(FacultyCandidateSource faculty, string? authorDisplayName)
    {
        if (string.IsNullOrWhiteSpace(faculty.DisplayName) || string.IsNullOrWhiteSpace(authorDisplayName))
        {
            return 0m;
        }

        var facultyFirst = NormalizeName(faculty.FirstName);
        var facultyLast = NormalizeName(faculty.LastName);

        if (string.IsNullOrWhiteSpace(facultyFirst) || string.IsNullOrWhiteSpace(facultyLast))
        {
            var parsed = ParseName(faculty.DisplayName);
            facultyFirst = parsed.First;
            facultyLast = parsed.Last;
        }

        var authorParsed = ParseName(authorDisplayName);
        var authorFirst = authorParsed.First;
        var authorLast = authorParsed.Last;

        var facultyFull = NormalizeName(faculty.DisplayName);
        var authorFull = NormalizeName(authorDisplayName);

        if (!string.IsNullOrWhiteSpace(facultyFull) && facultyFull == authorFull)
        {
            return 100m;
        }

        if (facultyLast == authorLast && facultyFirst == authorFirst && !string.IsNullOrWhiteSpace(facultyFirst))
        {
            return 95m;
        }

        if (facultyLast == authorLast && FirstInitialMatches(facultyFirst, authorFirst))
        {
            return 80m;
        }

        if (facultyLast == authorLast && !string.IsNullOrWhiteSpace(facultyFirst) && authorFull.Contains(facultyFirst))
        {
            return 75m;
        }

        if (facultyLast == authorLast)
        {
            return 55m;
        }

        if (!string.IsNullOrWhiteSpace(facultyFirst) &&
            !string.IsNullOrWhiteSpace(facultyLast) &&
            authorFull.Contains(facultyFirst) &&
            authorFull.Contains(facultyLast))
        {
            return 85m;
        }

        return 0m;
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

    private static bool FirstInitialMatches(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               char.ToUpperInvariant(left[0]) == char.ToUpperInvariant(right[0]);
    }

    private static bool OrcidEquals(string? left, string? right)
    {
        var l = NormalizeOrcid(left);
        var r = NormalizeOrcid(right);
        return !string.IsNullOrWhiteSpace(l) && string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
    }

    private static bool OpenAlexIdEquals(string? left, string? right)
    {
        var leftFull = NormalizeOpenAlexId(left);
        var rightFull = NormalizeOpenAlexId(right);
        var leftShort = GetOpenAlexShortId(left);
        var rightShort = GetOpenAlexShortId(right);

        return (!string.IsNullOrWhiteSpace(leftFull) && string.Equals(leftFull, rightFull, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(leftShort) && string.Equals(leftShort, rightShort, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeOrcid(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
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
}
