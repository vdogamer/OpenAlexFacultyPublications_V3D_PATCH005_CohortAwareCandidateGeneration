using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class FacultyIdentityCandidateSummaryGenerationJob
{
    private readonly CandidateGenerationRepository _candidateGenerationRepository;
    private readonly FacultyIdentityCandidateSummaryRepository _summaryRepository;
    private readonly FacultyManualReviewRuleRepository _manualReviewRuleRepository;
    private readonly IOperationalLogger _logger;
    private readonly CandidateGenerationOptions _options;

    public FacultyIdentityCandidateSummaryGenerationJob(
        CandidateGenerationRepository candidateGenerationRepository,
        FacultyIdentityCandidateSummaryRepository summaryRepository,
        FacultyManualReviewRuleRepository manualReviewRuleRepository,
        IOperationalLogger logger,
        IOptions<CandidateGenerationOptions> options)
    {
        _candidateGenerationRepository = candidateGenerationRepository;
        _summaryRepository = summaryRepository;
        _manualReviewRuleRepository = manualReviewRuleRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var selectedCohortName = NormalizeCohortName(_options.PilotCohortName);
        var institutionFilter = selectedCohortName is null
            ? "FacultyIdentityCandidateSummaryGeneration"
            : $"FacultyIdentityCandidateSummaryGeneration; PilotCohortName={selectedCohortName}";

        var run = await _candidateGenerationRepository.StartRunAsync(
            _options.RunMode,
            institutionFilter,
            null,
            _options.MaxPublicationCandidatesToGenerate,
            _options.ApplicationVersion,
            _options.StartedBy,
            cancellationToken);

        await _logger.LogInfoAsync(
            "FacultyIdentityCandidateSummaryGeneration",
            "RunStarted",
            $"Started identity candidate summary generation run {run.IngestionRunId}. PilotCohortName={selectedCohortName ?? "(none)"}.",
            run.RunCorrelationId,
            cancellationToken);

        try
        {
            var faculties = selectedCohortName is null
                ? await _candidateGenerationRepository.LoadFacultyAsync(_options.MaxFacultyToProcess, cancellationToken)
                : await _candidateGenerationRepository.LoadFacultyForPilotCohortAsync(selectedCohortName, _options.MaxFacultyToProcess, cancellationToken);

            var selectedFacultyIds = faculties.Select(f => f.FacultyId).ToList();
            var deletedStaleRows = await _summaryRepository.DeleteForFacultyIdsAsync(selectedFacultyIds, cancellationToken);
            var sources = await _summaryRepository.LoadSummarySourcesAsync(selectedFacultyIds, cancellationToken);
            var manualReviewRules = await _manualReviewRuleRepository.LoadActiveRulesAsync(selectedFacultyIds, cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyIdentityCandidateSummaryGeneration",
                "SummarySourceDiagnostics",
                $"SelectedCohortName={selectedCohortName ?? "(none)"}; EnabledCohortFacultyCount={(selectedCohortName is null ? 0 : faculties.Count)}; FacultyIdsSelected={FormatIdList(selectedFacultyIds)}; SummarySourcesSelected={sources.Count}; DeletedStaleSummaryRows={deletedStaleRows}; ManualReviewRuleCount={manualReviewRules.Count}.",
                run.RunCorrelationId,
                cancellationToken);

            var inserted = 0;
            var updated = 0;
            var statusCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var summaryCountsByFaculty = selectedFacultyIds.ToDictionary(facultyId => facultyId, _ => 0);
            var ruleAppliedCount = 0;

            foreach (var source in sources)
            {
                manualReviewRules.TryGetValue(source.FacultyId, out var manualReviewRule);
                if (manualReviewRule is not null)
                {
                    ruleAppliedCount++;
                }

                var summary = BuildSummary(source, run.IngestionRunId, manualReviewRule);
                var result = await _summaryRepository.UpsertAsync(summary, cancellationToken);

                if (result.Inserted) inserted++;
                if (result.Updated) updated++;

                summaryCountsByFaculty[source.FacultyId] = summaryCountsByFaculty.GetValueOrDefault(source.FacultyId) + 1;
                statusCounts[summary.IdentityReviewTriageStatus] = statusCounts.GetValueOrDefault(summary.IdentityReviewTriageStatus) + 1;
            }

            var facultyWithZeroSummaries = summaryCountsByFaculty
                .Where(pair => pair.Value == 0)
                .Select(pair => pair.Key)
                .OrderBy(facultyId => facultyId)
                .ToList();

            var summaryText =
                $"Faculty identity candidate summary generation completed. SelectedCohortName={selectedCohortName ?? "(none)"}; " +
                $"EnabledCohortFacultyCount={(selectedCohortName is null ? 0 : faculties.Count)}; FacultyIdsSelected={FormatIdList(selectedFacultyIds)}; " +
                $"SummarySourcesSelected={sources.Count}; DeletedStaleSummaryRows={deletedStaleRows}; SummaryRowsInserted={inserted}; SummaryRowsUpdated={updated}; " +
                $"ManualReviewRuleCount={manualReviewRules.Count}; SummaryRowsWithManualReviewRule={ruleAppliedCount}; " +
                $"StatusCounts={FormatCounts(statusCounts)}; SummaryCountsByFaculty={FormatCounts(summaryCountsByFaculty)}; FacultyWithZeroSummaries={FormatIdList(facultyWithZeroSummaries)}.";

            await _candidateGenerationRepository.CompleteRunAsync(
                run.IngestionRunId,
                summaryText,
                authorsProcessed: sources.Count,
                worksProcessed: sources.Sum(s => s.ScoredWorkRows),
                cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyIdentityCandidateSummaryGeneration",
                "RunCompleted",
                summaryText,
                run.RunCorrelationId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _candidateGenerationRepository.FailRunAsync(run.IngestionRunId, ex, CancellationToken.None);

            await _logger.LogErrorAsync(
                "FacultyIdentityCandidateSummaryGeneration",
                "RunFailed",
                $"Faculty identity candidate summary generation run {run.IngestionRunId} failed.",
                ex,
                run.RunCorrelationId,
                CancellationToken.None);

            throw;
        }
    }

    private static FacultyIdentityCandidateSummaryWriteModel BuildSummary(
        FacultyIdentityCandidateSummarySource source,
        long ingestionRunId,
        FacultyManualReviewRule? manualReviewRule)
    {
        var triageStatus = GetTriageStatus(source, manualReviewRule);
        var reviewPriority = GetReviewPriority(triageStatus, source, manualReviewRule);
        var hasVerifiedIdentitySeed = HasVerifiedIdentitySeed(source);
        var hasLikelyIdentityWorkVolume = HasLikelyIdentityWorkVolume(source);
        var hasManualReviewRule = manualReviewRule is not null;
        var isAbbreviatedSearchCandidate = IsAbbreviatedSearchCandidate(source);

        var summaryText =
            $"TriageStatus={triageStatus}; ReviewPriority={reviewPriority}; Faculty='{source.FacultyDisplayName}'; " +
            $"CandidateAuthor={source.CandidateAuthorOpenAlexShortId}; CandidateDisplayName='{source.CandidateDisplayName}'; " +
            $"CandidateType={source.BestCandidateType}; IdentityScore={source.BestIdentityEvidenceScore}; " +
            $"HasVerifiedIdentitySeed={hasVerifiedIdentitySeed}; HasLikelyIdentityWorkVolume={hasLikelyIdentityWorkVolume}; " +
            $"HasManualReviewRule={hasManualReviewRule}; IsAbbreviatedSearchCandidate={isAbbreviatedSearchCandidate}; " +
            $"RequireVerifiedSeedForLikelyStatus={manualReviewRule?.RequireVerifiedSeedForLikelyStatus ?? false}; UseManualReviewForAbbreviatedSearch={manualReviewRule?.UseManualReviewForAbbreviatedSearch ?? false}; " +
            $"RequireAuthorAffiliationSupport={manualReviewRule?.RequireAuthorAffiliationSupport ?? false}; RequireCollaborationOrTopicSupport={manualReviewRule?.RequireCollaborationOrTopicSupport ?? false}; ManualReviewRuleReason='{manualReviewRule?.Reason}'; " +
            $"DiscoveryMethods={source.DiscoveryMethods}; DistinctExpandedWorks={source.DistinctExpandedWorks}; ScoredWorkRows={source.ScoredWorkRows}; " +
            $"StrongWorkEvidenceRows={source.StrongWorkEvidenceRows}; ModerateWorkEvidenceRows={source.ModerateWorkEvidenceRows}; WeakWorkEvidenceRows={source.WeakWorkEvidenceRows}; ConflictingWorkEvidenceRows={source.ConflictingWorkEvidenceRows}; " +
            $"AverageWorkEvidenceScore={source.AverageWorkEvidenceScore}; BestWorkEvidenceScore={source.BestWorkEvidenceScore}; PublicationYears={source.EarliestPublicationYear}-{source.LatestPublicationYear}.";

        var evidence = new
        {
            source.FacultyId,
            source.FacultyDisplayName,
            source.CandidateAuthorOpenAlexId,
            source.CandidateAuthorOpenAlexShortId,
            source.CandidateDisplayName,
            source.CandidateOrcid,
            source.WorksCount,
            source.CitedByCount,
            source.DiscoveryMethodCount,
            source.DiscoveryMethods,
            source.BestIdentityEvidenceScore,
            source.BestIdentityEvidenceStatus,
            source.BestCandidateType,
            source.ExpandedWorkRows,
            source.DistinctExpandedWorks,
            source.ScoredWorkRows,
            source.StrongWorkEvidenceRows,
            source.ModerateWorkEvidenceRows,
            source.WeakWorkEvidenceRows,
            source.ConflictingWorkEvidenceRows,
            source.AverageWorkEvidenceScore,
            source.BestWorkEvidenceScore,
            source.EarliestPublicationYear,
            source.LatestPublicationYear,
            HasVerifiedIdentitySeed = hasVerifiedIdentitySeed,
            HasLikelyIdentityWorkVolume = hasLikelyIdentityWorkVolume,
            HasManualReviewRule = hasManualReviewRule,
            IsAbbreviatedSearchCandidate = isAbbreviatedSearchCandidate,
            ManualReviewRule = manualReviewRule,
            IdentityReviewTriageStatus = triageStatus,
            ReviewPriority = reviewPriority
        };

        return new FacultyIdentityCandidateSummaryWriteModel
        {
            FacultyId = source.FacultyId,
            FacultyDisplayName = source.FacultyDisplayName,
            CandidateAuthorOpenAlexId = source.CandidateAuthorOpenAlexId,
            CandidateAuthorOpenAlexShortId = source.CandidateAuthorOpenAlexShortId,
            CandidateDisplayName = source.CandidateDisplayName,
            CandidateOrcid = source.CandidateOrcid,
            WorksCount = source.WorksCount,
            CitedByCount = source.CitedByCount,
            DiscoveryMethodCount = source.DiscoveryMethodCount,
            DiscoveryMethods = source.DiscoveryMethods,
            BestIdentityEvidenceScore = source.BestIdentityEvidenceScore,
            BestIdentityEvidenceStatus = source.BestIdentityEvidenceStatus,
            BestCandidateType = source.BestCandidateType,
            ExpandedWorkRows = source.ExpandedWorkRows,
            DistinctExpandedWorks = source.DistinctExpandedWorks,
            ScoredWorkRows = source.ScoredWorkRows,
            StrongWorkEvidenceRows = source.StrongWorkEvidenceRows,
            ModerateWorkEvidenceRows = source.ModerateWorkEvidenceRows,
            WeakWorkEvidenceRows = source.WeakWorkEvidenceRows,
            ConflictingWorkEvidenceRows = source.ConflictingWorkEvidenceRows,
            AverageWorkEvidenceScore = source.AverageWorkEvidenceScore,
            BestWorkEvidenceScore = source.BestWorkEvidenceScore,
            EarliestPublicationYear = source.EarliestPublicationYear,
            LatestPublicationYear = source.LatestPublicationYear,
            IdentityReviewTriageStatus = triageStatus,
            ReviewPriority = reviewPriority,
            SummaryText = summaryText,
            EvidenceJson = JsonSerializer.Serialize(evidence),
            IngestionRunId = ingestionRunId
        };
    }

    private static string GetTriageStatus(FacultyIdentityCandidateSummarySource source, FacultyManualReviewRule? manualReviewRule)
    {
        var bestWorkScore = source.BestWorkEvidenceScore ?? 0m;
        var averageWorkScore = source.AverageWorkEvidenceScore ?? 0m;
        var hasVerifiedIdentitySeed = HasVerifiedIdentitySeed(source);
        var hasLikelyIdentityWorkVolume = HasLikelyIdentityWorkVolume(source);
        var hasStrongLikelyWorkEvidence = bestWorkScore >= 75m && averageWorkScore >= 65m;
        var isAbbreviatedSearchCandidate = IsAbbreviatedSearchCandidate(source);

        if (source.ConflictingWorkEvidenceRows > 0 &&
            source.StrongWorkEvidenceRows == 0 &&
            source.ModerateWorkEvidenceRows == 0)
        {
            return "LikelyFalsePositive";
        }

        if (manualReviewRule?.UseManualReviewForAbbreviatedSearch == true &&
            isAbbreviatedSearchCandidate &&
            !hasVerifiedIdentitySeed)
        {
            return "NeedsManualReview";
        }

        if (source.BestCandidateType == "PrimaryIdentityCandidate" &&
            source.BestIdentityEvidenceScore >= 90m &&
            source.StrongWorkEvidenceRows > 0 &&
            (hasVerifiedIdentitySeed || (hasLikelyIdentityWorkVolume && hasStrongLikelyWorkEvidence)))
        {
            if (manualReviewRule?.RequireVerifiedSeedForLikelyStatus == true && !hasVerifiedIdentitySeed)
            {
                return "NeedsManualReview";
            }

            return "LikelyIdentity";
        }

        if (source.BestCandidateType == "PossibleProfileFragment" &&
            (hasVerifiedIdentitySeed || HasPossibleSplitProfileEvidence(source, bestWorkScore, averageWorkScore)))
        {
            return "PossibleSplitProfile";
        }

        if (source.BestIdentityEvidenceScore >= 70m &&
            (source.StrongWorkEvidenceRows > 0 || source.ModerateWorkEvidenceRows > 0 || averageWorkScore >= 60m))
        {
            return "NeedsManualReview";
        }

        if (source.BestIdentityEvidenceScore < 55m || bestWorkScore < 45m)
        {
            return "WeakCandidate";
        }

        return "NeedsManualReview";
    }

    private static bool HasVerifiedIdentitySeed(FacultyIdentityCandidateSummarySource source)
    {
        var discoveryMethods = source.DiscoveryMethods ?? string.Empty;

        return discoveryMethods.Contains("ClaimedOpenAlexAuthorId", StringComparison.OrdinalIgnoreCase)
            || discoveryMethods.Contains("OrcidLookup", StringComparison.OrdinalIgnoreCase)
            || string.Equals(source.BestIdentityEvidenceStatus, "StrongIdentifierCandidate", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAbbreviatedSearchCandidate(FacultyIdentityCandidateSummarySource source)
    {
        var discoveryMethods = source.DiscoveryMethods ?? string.Empty;
        return discoveryMethods.Contains("FirstInitialLastName", StringComparison.OrdinalIgnoreCase)
            || discoveryMethods.Contains("Initial", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasLikelyIdentityWorkVolume(FacultyIdentityCandidateSummarySource source)
    {
        return source.DistinctExpandedWorks >= 5
            && source.ScoredWorkRows >= 5
            && source.StrongWorkEvidenceRows >= 3;
    }

    private static bool HasPossibleSplitProfileEvidence(
        FacultyIdentityCandidateSummarySource source,
        decimal bestWorkScore,
        decimal averageWorkScore)
    {
        if (source.DistinctExpandedWorks < 5 || source.ScoredWorkRows < 5)
        {
            return false;
        }

        return source.StrongWorkEvidenceRows >= 2
            || source.ModerateWorkEvidenceRows >= 5
            || (bestWorkScore >= 75m && averageWorkScore >= 65m);
    }

    private static int GetReviewPriority(string triageStatus, FacultyIdentityCandidateSummarySource source, FacultyManualReviewRule? manualReviewRule)
    {
        if (manualReviewRule is not null && string.Equals(triageStatus, "NeedsManualReview", StringComparison.OrdinalIgnoreCase))
        {
            return 25;
        }

        return triageStatus switch
        {
            "LikelyIdentity" => 10,
            "PossibleSplitProfile" => 20,
            "NeedsManualReview" => 30,
            "WeakCandidate" => 80,
            "LikelyFalsePositive" => 90,
            _ => 50
        };
    }

    private static string? NormalizeCohortName(string? cohortName)
    {
        return string.IsNullOrWhiteSpace(cohortName) ? null : cohortName.Trim();
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
