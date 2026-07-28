using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class FacultyAuthorIdentityResolutionPreviewJob
{
    private readonly FacultyAuthorIdentityResolutionPreviewRepository _repository;
    private readonly FacultyAuthorIdentityResolutionSourceRepository _sourceRepository;
    private readonly IOperationalLogger _logger;
    private readonly FacultyAuthorIdentityResolutionPreviewOptions _options;

    public FacultyAuthorIdentityResolutionPreviewJob(
        FacultyAuthorIdentityResolutionPreviewRepository repository,
        FacultyAuthorIdentityResolutionSourceRepository sourceRepository,
        IOperationalLogger logger,
        IOptions<FacultyAuthorIdentityResolutionPreviewOptions> options)
    {
        _repository = repository;
        _sourceRepository = sourceRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var sourceRunId = _options.SourceWorkBackfillRunId
            ?? throw new InvalidOperationException("FacultyAuthorIdentityResolutionPreview:SourceWorkBackfillRunId is required. Use an explicit completed PATCH017B1 run ID.");

        var maxWorks = Math.Clamp(_options.MaxWorks, 1, 5);
        var safetyBefore = await _repository.GetSafetyCountsAsync(cancellationToken);
        var previewRunId = await _repository.StartRunAsync(
            sourceRunId,
            _options.FacultyId,
            maxWorks,
            safetyBefore,
            _options.StartedBy,
            _options.ApplicationVersion,
            cancellationToken);

        await _logger.LogInfoAsync(
            "FacultyAuthorIdentityResolutionPreview",
            "RunStarted",
            $"Started PATCH017B3 identity resolution preview run {previewRunId}. SourceWorkBackfillRunId={sourceRunId}; FacultyId={_options.FacultyId}; MaxWorks={maxWorks}.",
            cancellationToken: cancellationToken);

        try
        {
            var sourceRows = await _sourceRepository.LoadSourceRowsAsync(sourceRunId, _options.FacultyId, maxWorks, cancellationToken);
            if (sourceRows.Count == 0)
            {
                throw new InvalidOperationException($"No authorships were found for FacultyId={_options.FacultyId} in completed backfill run {sourceRunId}.");
            }

            foreach (var row in sourceRows)
            {
                row.ExactFacultyOrcidMatch = OrcidEquals(row.FacultyOrcid, row.CandidateAuthorOrcid);
                row.ExactAcademicOnlineOrcidMatch = OrcidEquals(row.AcademicOnlineOrcidUrl, row.CandidateAuthorOrcid);
                row.ExactClaimedOpenAlexIdMatch = OpenAlexIdEquals(row.FacultyClaimedOpenAlexAuthorId, row.CandidateAuthorOpenAlexId);
                row.NameEvidenceScore = ComputeNameEvidenceScore(
                    row.FacultyDisplayName,
                    row.FacultyFirstName,
                    row.FacultyLastName,
                    row.AcademicOnlineName,
                    row.CandidateAuthorDisplayName);
                row.AppearsOnAllSelectedWorks = row.SourceWorkCount > 0 && row.WorkAppearanceCount == row.SourceWorkCount;
                row.ProposedIdentityScore = ComputeIdentityScore(row);
            }

            var candidates = sourceRows
                .Where(HasMeaningfulIdentitySignal)
                .OrderByDescending(row => row.ProposedIdentityScore)
                .ThenByDescending(row => row.WorkAppearanceCount)
                .ThenByDescending(row => row.UvaAffiliationWorkCount)
                .ThenBy(row => row.CandidateAuthorDisplayName)
                .ToList();

            if (candidates.Count == 0)
            {
                candidates = sourceRows
                    .OrderByDescending(row => row.WorkAppearanceCount)
                    .ThenByDescending(row => row.NameEvidenceScore)
                    .ThenByDescending(row => row.UvaAffiliationWorkCount)
                    .Take(5)
                    .ToList();
            }

            var strongCandidates = candidates.Count(IsStrongCandidate);
            var ambiguous = strongCandidates > 1;

            for (var index = 0; index < candidates.Count; index++)
            {
                var row = candidates[index];
                row.CandidateRank = index + 1;
                row.PreviewStatus = DeterminePreviewStatus(row, ambiguous);
                row.ProposedNextAction = DetermineNextAction(row);
                row.EvidenceSummary = BuildEvidenceSummary(row);
                row.EvidenceJson = JsonSerializer.Serialize(new
                {
                    row.SourceWorkBackfillRunId,
                    row.FacultyId,
                    row.FacultyDisplayName,
                    row.FacultyOrcid,
                    row.FacultyClaimedOpenAlexAuthorId,
                    row.AcademicOnlineName,
                    row.AcademicOnlineEmail,
                    row.AcademicOnlineOrcidUrl,
                    row.SourceWorkCount,
                    row.CandidateAuthorOpenAlexId,
                    row.CandidateAuthorDisplayName,
                    row.CandidateAuthorOrcid,
                    row.WorkAppearanceCount,
                    row.AppearsOnAllSelectedWorks,
                    row.CorrespondingAuthorWorkCount,
                    row.AuthorshipInstitutionCount,
                    row.UvaAffiliationWorkCount,
                    row.AppearingWorkOpenAlexIds,
                    row.ExactFacultyOrcidMatch,
                    row.ExactAcademicOnlineOrcidMatch,
                    row.ExactClaimedOpenAlexIdMatch,
                    row.NameEvidenceScore,
                    row.ExistingIdentitySummaryId,
                    row.ExistingIdentityTriageStatus,
                    row.ExistingIdentityEvidenceScore,
                    row.CurrentIdentityDecisionStatus,
                    row.ExistingIdentityCandidateDiscoveryCount,
                    row.CurrentAcceptedAuthorDecisionCount,
                    row.ProposedIdentityScore,
                    row.CandidateRank,
                    row.PreviewStatus,
                    row.ProposedNextAction,
                    PreviewOnly = true
                });

                await _repository.SaveCandidateAsync(previewRunId, row, cancellationToken);
            }

            var safetyAfter = await _repository.GetSafetyCountsAsync(cancellationToken);
            var safetyUnchanged =
                safetyBefore.IdentityCandidateRows == safetyAfter.IdentityCandidateRows &&
                safetyBefore.IdentityDecisionRows == safetyAfter.IdentityDecisionRows &&
                safetyBefore.AuthorDecisionRows == safetyAfter.AuthorDecisionRows;

            var selectedWorkCount = sourceRows.Max(row => row.SourceWorkCount);
            var exactOrcidCandidates = candidates.Count(row => row.ExactFacultyOrcidMatch || row.ExactAcademicOnlineOrcidMatch);
            var summary =
                $"PATCH017B3 identity resolution preview completed. SourceWorkBackfillRunId={sourceRunId}; FacultyId={_options.FacultyId}; " +
                $"SelectedWorks={selectedWorkCount}; EvaluatedCandidates={candidates.Count}; StrongCandidates={strongCandidates}; " +
                $"ExactOrcidCandidates={exactOrcidCandidates}; AmbiguousStrongCandidates={(ambiguous ? strongCandidates : 0)}; " +
                $"IdentityCandidateRowsBefore={safetyBefore.IdentityCandidateRows}; IdentityCandidateRowsAfter={safetyAfter.IdentityCandidateRows}; " +
                $"IdentityDecisionRowsBefore={safetyBefore.IdentityDecisionRows}; IdentityDecisionRowsAfter={safetyAfter.IdentityDecisionRows}; " +
                $"AuthorDecisionRowsBefore={safetyBefore.AuthorDecisionRows}; AuthorDecisionRowsAfter={safetyAfter.AuthorDecisionRows}; " +
                $"SafetyUnchanged={safetyUnchanged}. No identity candidate, identity decision, or author decision write method was invoked.";

            await _repository.CompleteRunAsync(
                previewRunId,
                selectedWorkCount,
                candidates.Count,
                strongCandidates,
                exactOrcidCandidates,
                ambiguous ? strongCandidates : 0,
                safetyAfter,
                summary,
                cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyAuthorIdentityResolutionPreview",
                "RunCompleted",
                summary,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await _repository.FailRunAsync(previewRunId, ex, CancellationToken.None);
            await _logger.LogErrorAsync(
                "FacultyAuthorIdentityResolutionPreview",
                "RunFailed",
                $"PATCH017B3 identity resolution preview run {previewRunId} failed.",
                ex,
                cancellationToken: CancellationToken.None);
            throw;
        }
    }

    private static bool HasMeaningfulIdentitySignal(FacultyAuthorIdentityResolutionPreviewSourceRow row)
    {
        return row.ExactFacultyOrcidMatch
            || row.ExactAcademicOnlineOrcidMatch
            || row.ExactClaimedOpenAlexIdMatch
            || row.NameEvidenceScore >= 7
            || row.ExistingIdentitySummaryId.HasValue
            || !string.IsNullOrWhiteSpace(row.CurrentIdentityDecisionStatus)
            || row.CurrentAcceptedAuthorDecisionCount > 0
            || row.AppearsOnAllSelectedWorks;
    }

    private static bool IsStrongCandidate(FacultyAuthorIdentityResolutionPreviewSourceRow row)
    {
        return string.Equals(row.CurrentIdentityDecisionStatus, "AcceptedIdentity", StringComparison.OrdinalIgnoreCase)
            || row.CurrentAcceptedAuthorDecisionCount > 0
            || row.ProposedIdentityScore >= 80m;
    }

    private static decimal ComputeIdentityScore(FacultyAuthorIdentityResolutionPreviewSourceRow row)
    {
        decimal score = 0m;
        if (row.ExactClaimedOpenAlexIdMatch) score += 60m;
        if (row.ExactFacultyOrcidMatch) score += 50m;
        if (row.ExactAcademicOnlineOrcidMatch) score += 20m;

        if (row.AppearsOnAllSelectedWorks)
        {
            score += 15m;
        }
        else if (row.SourceWorkCount > 0)
        {
            score += Math.Round(10m * row.WorkAppearanceCount / row.SourceWorkCount, 2);
        }

        if (row.UvaAffiliationWorkCount > 0) score += 10m;
        score += row.NameEvidenceScore;
        if ((row.ExistingIdentityEvidenceScore ?? 0m) >= 80m) score += 5m;
        if (string.Equals(row.CurrentIdentityDecisionStatus, "AcceptedIdentity", StringComparison.OrdinalIgnoreCase) ||
            row.CurrentAcceptedAuthorDecisionCount > 0)
        {
            score = 100m;
        }

        return Math.Min(100m, score);
    }

    private static string DeterminePreviewStatus(FacultyAuthorIdentityResolutionPreviewSourceRow row, bool ambiguous)
    {
        if (string.Equals(row.CurrentIdentityDecisionStatus, "AcceptedIdentity", StringComparison.OrdinalIgnoreCase) ||
            row.CurrentAcceptedAuthorDecisionCount > 0)
        {
            return "ExistingAcceptedIdentity";
        }
        if (ambiguous && IsStrongCandidate(row)) return "MultipleStrongCandidatesNeedReview";
        if ((row.ExactFacultyOrcidMatch || row.ExactAcademicOnlineOrcidMatch) && row.AppearsOnAllSelectedWorks)
        {
            return "ExactOrcidAndAuthorshipMatch";
        }
        if (row.ProposedIdentityScore >= 80m) return "StrongIdentityCandidate";
        if (row.ExistingIdentitySummaryId.HasValue) return "ExistingIdentityCandidateNeedsReview";
        if (row.AppearsOnAllSelectedWorks && row.NameEvidenceScore >= 7) return "ConsistentAuthorshipNeedsReview";
        return "CandidateNeedsMoreEvidence";
    }

    private static string DetermineNextAction(FacultyAuthorIdentityResolutionPreviewSourceRow row)
    {
        return row.PreviewStatus switch
        {
            "ExistingAcceptedIdentity" => "IdentityAlreadyAccepted_RerunPATCH017B2",
            "MultipleStrongCandidatesNeedReview" => "ManualIdentityInvestigation",
            "ExactOrcidAndAuthorshipMatch" when row.ExistingIdentitySummaryId.HasValue => "ReviewExistingIdentityCandidate",
            "ExactOrcidAndAuthorshipMatch" => "RunIdentityCandidatePipelineForFaculty",
            "StrongIdentityCandidate" when row.ExistingIdentitySummaryId.HasValue => "ReviewExistingIdentityCandidate",
            "StrongIdentityCandidate" => "RunIdentityCandidatePipelineForFaculty",
            "ExistingIdentityCandidateNeedsReview" => "ReviewExistingIdentityCandidate",
            _ => "GatherMoreIdentityEvidence"
        };
    }

    private static string BuildEvidenceSummary(FacultyAuthorIdentityResolutionPreviewSourceRow row)
    {
        return
            $"PATCH017B3 preview only: Faculty='{row.FacultyDisplayName}' ({row.FacultyId}); " +
            $"FacultyOrcid='{NormalizeOrcid(row.FacultyOrcid) ?? "(none)"}'; AcademicOnlineOrcid='{NormalizeOrcid(row.AcademicOnlineOrcidUrl) ?? "(none)"}'; " +
            $"CandidateAuthor='{row.CandidateAuthorDisplayName ?? "(unknown)"}' ({row.CandidateAuthorOpenAlexId}); CandidateOrcid='{NormalizeOrcid(row.CandidateAuthorOrcid) ?? "(none)"}'; " +
            $"ExactFacultyOrcidMatch={row.ExactFacultyOrcidMatch}; ExactAcademicOnlineOrcidMatch={row.ExactAcademicOnlineOrcidMatch}; " +
            $"ExactClaimedOpenAlexIdMatch={row.ExactClaimedOpenAlexIdMatch}; NameEvidenceScore={row.NameEvidenceScore}; " +
            $"WorkAppearanceCount={row.WorkAppearanceCount}/{row.SourceWorkCount}; AppearsOnAllSelectedWorks={row.AppearsOnAllSelectedWorks}; " +
            $"UvaAffiliationWorkCount={row.UvaAffiliationWorkCount}; ExistingIdentitySummaryId={(row.ExistingIdentitySummaryId?.ToString() ?? "(none)")}; " +
            $"CurrentIdentityDecisionStatus={row.CurrentIdentityDecisionStatus ?? "(none)"}; ProposedIdentityScore={row.ProposedIdentityScore}; " +
            $"PreviewStatus={row.PreviewStatus}; NextAction={row.ProposedNextAction}. No identity or author decision was written.";
    }

    private static bool OrcidEquals(string? left, string? right)
    {
        var normalizedLeft = NormalizeOrcid(left);
        var normalizedRight = NormalizeOrcid(right);
        return normalizedLeft is not null && normalizedRight is not null &&
               string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOrcid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim()
            .Replace("https://orcid.org/", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://orcid.org/", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim('/')
            .ToUpperInvariant();
    }

    private static bool OpenAlexIdEquals(string? left, string? right)
    {
        var normalizedLeft = NormalizeOpenAlexId(left);
        var normalizedRight = NormalizeOpenAlexId(right);
        return normalizedLeft is not null && normalizedRight is not null &&
               string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOpenAlexId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim().TrimEnd('/');
        var slashIndex = trimmed.LastIndexOf('/');
        return (slashIndex >= 0 ? trimmed[(slashIndex + 1)..] : trimmed).ToUpperInvariant();
    }

    private static int ComputeNameEvidenceScore(
        string facultyDisplayName,
        string? facultyFirstName,
        string? facultyLastName,
        string? academicOnlineName,
        string? candidateName)
    {
        if (string.IsNullOrWhiteSpace(candidateName)) return 0;

        var candidate = NormalizeName(candidateName);
        var faculty = NormalizeName(facultyDisplayName);
        var academic = NormalizeName(academicOnlineName);
        if (!string.IsNullOrWhiteSpace(candidate) &&
            (candidate == faculty || (!string.IsNullOrWhiteSpace(academic) && candidate == academic)))
        {
            return 10;
        }

        var candidateTokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var candidateFirst = candidateTokens.FirstOrDefault();
        var candidateLast = candidateTokens.LastOrDefault();
        var first = NormalizeName(facultyFirstName);
        var last = NormalizeName(facultyLastName);

        if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last) &&
            string.Equals(candidateFirst, first, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidateLast, last, StringComparison.OrdinalIgnoreCase))
        {
            return 8;
        }
        if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last) &&
            candidateFirst?.StartsWith(first[..1], StringComparison.OrdinalIgnoreCase) == true &&
            string.Equals(candidateLast, last, StringComparison.OrdinalIgnoreCase))
        {
            return 6;
        }
        if (!string.IsNullOrWhiteSpace(last) && string.Equals(candidateLast, last, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }
        return 0;
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var cleaned = new string(value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        return string.Join(' ', cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 1));
    }
}
