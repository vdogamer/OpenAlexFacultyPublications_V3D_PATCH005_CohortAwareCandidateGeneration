using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class FacultyPublicationCandidatePreviewJob
{
    private readonly FacultyPublicationCandidatePreviewRepository _repository;
    private readonly IOperationalLogger _logger;
    private readonly FacultyPublicationCandidatePreviewOptions _options;

    public FacultyPublicationCandidatePreviewJob(
        FacultyPublicationCandidatePreviewRepository repository,
        IOperationalLogger logger,
        IOptions<FacultyPublicationCandidatePreviewOptions> options)
    {
        _repository = repository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var sourceRunId = _options.SourceWorkBackfillRunId
            ?? throw new InvalidOperationException("FacultyPublicationCandidatePreview:SourceWorkBackfillRunId is required. Use an explicit completed PATCH017B1 backfill run ID.");

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
            "FacultyPublicationCandidatePreview",
            "RunStarted",
            $"Started PATCH017B6 split-identity-aware candidate preview run {previewRunId}. SourceWorkBackfillRunId={sourceRunId}; FacultyId={_options.FacultyId}; MaxWorks={maxWorks}.",
            cancellationToken: cancellationToken);

        try
        {
            var rows = await _repository.LoadSourceRowsAsync(sourceRunId, _options.FacultyId, maxWorks, cancellationToken);

            var ready = 0;
            var existing = 0;
            var noDecision = 0;
            var multipleDecisions = 0;
            var authorNotOnWork = 0;

            foreach (var row in rows)
            {
                row.ProposedMatchScore = ComputePublicationScore(row);
                row.PreviewStatus = DeterminePreviewStatus(row);
                row.EvidenceSummary = BuildEvidenceSummary(row);
                row.EvidenceJson = JsonSerializer.Serialize(new
                {
                    Patch = "PATCH017B6",
                    SplitIdentityAware = true,
                    row.SourceWorkBackfillRunId,
                    row.SourceBackfillItemId,
                    row.FacultyId,
                    row.FacultyDisplayName,
                    row.WorkId,
                    row.WorkOpenAlexId,
                    row.WorkTitle,
                    AcceptedFacultyIdentityCount = row.AcceptedDecisionCount,
                    row.FacultyAuthorMatchDecisionId,
                    SelectedAcceptedAuthorOpenAlexId = row.AuthorOpenAlexId,
                    SelectedAcceptedAuthorDisplayName = row.AuthorDisplayName,
                    SelectedAcceptedAuthorOrcid = row.AuthorOrcid,
                    SelectedAcceptedAuthorAppearsOnWork = row.WorkAuthorshipId.HasValue,
                    row.WorkAuthorshipId,
                    row.OrdinalPosition,
                    row.IsCorresponding,
                    row.AuthorshipInstitutionCount,
                    row.MatchingInstitutionCount,
                    row.ExistingCandidateId,
                    ProposedCandidateSource = "AcceptedAuthorMatch",
                    ProposedCandidateStatus = "PendingReview",
                    row.ProposedMatchScore,
                    row.PreviewStatus
                });

                switch (row.PreviewStatus)
                {
                    case "ReadyToGenerate": ready++; break;
                    case "AlreadyCandidateExists": existing++; break;
                    case "NoAcceptedAuthorDecision": noDecision++; break;
                    case "MultipleAcceptedAuthorDecisions": multipleDecisions++; break;
                    case "AcceptedAuthorNotOnWork": authorNotOnWork++; break;
                }

                await _repository.SaveItemAsync(previewRunId, row, cancellationToken);
            }

            var safetyAfter = await _repository.GetSafetyCountsAsync(cancellationToken);
            var safetyUnchanged =
                safetyBefore.CandidateRows == safetyAfter.CandidateRows &&
                safetyBefore.DecisionRows == safetyAfter.DecisionRows;

            var summary =
                $"PATCH017B6 split-identity-aware candidate preview completed. SourceWorkBackfillRunId={sourceRunId}; FacultyId={_options.FacultyId}; " +
                $"Selected={rows.Count}; ReadyToGenerate={ready}; ExistingCandidate={existing}; NoAcceptedAuthorDecision={noDecision}; " +
                $"MultipleAcceptedAuthorDecisions={multipleDecisions}; AcceptedAuthorNotOnWork={authorNotOnWork}; " +
                $"CandidateRowsBefore={safetyBefore.CandidateRows}; CandidateRowsAfter={safetyAfter.CandidateRows}; " +
                $"DecisionRowsBefore={safetyBefore.DecisionRows}; DecisionRowsAfter={safetyAfter.DecisionRows}; " +
                $"CandidateAndDecisionSafetyUnchanged={safetyUnchanged}. Multiple accepted faculty identities are allowed when the selected accepted identity appears on the specific work. No candidate-generation or decision-write method was invoked.";

            await _repository.CompleteRunAsync(
                previewRunId,
                rows.Count,
                ready,
                existing,
                noDecision,
                multipleDecisions,
                authorNotOnWork,
                safetyAfter,
                summary,
                cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyPublicationCandidatePreview",
                "RunCompleted",
                summary,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await _repository.FailRunAsync(previewRunId, ex, CancellationToken.None);
            await _logger.LogErrorAsync(
                "FacultyPublicationCandidatePreview",
                "RunFailed",
                $"PATCH017B6 split-identity-aware candidate preview run {previewRunId} failed.",
                ex,
                cancellationToken: CancellationToken.None);
            throw;
        }
    }

    private static string DeterminePreviewStatus(FacultyPublicationCandidatePreviewSourceRow row)
    {
        if (row.ExistingCandidateId.HasValue)
        {
            return "AlreadyCandidateExists";
        }

        if (row.AcceptedDecisionCount == 0 || !row.FacultyAuthorMatchDecisionId.HasValue)
        {
            return "NoAcceptedAuthorDecision";
        }

        // PATCH017B6: A faculty member may legitimately have multiple accepted OpenAlex
        // identities. The repository preferentially selects an accepted identity that
        // appears on the specific work. Once that authorship is verified, the publication
        // is eligible for generation regardless of how many accepted identities the
        // faculty member has overall.
        if (row.WorkAuthorshipId.HasValue)
        {
            return "ReadyToGenerate";
        }

        return "AcceptedAuthorNotOnWork";
    }

    private static decimal ComputePublicationScore(FacultyPublicationCandidatePreviewSourceRow row)
    {
        if (!row.WorkAuthorshipId.HasValue)
        {
            return 0m;
        }

        var score = 80m;
        if (row.IsCorresponding == true) score += 5m;
        if (row.MatchingInstitutionCount > 0) score += 10m;
        if (row.AuthorshipInstitutionCount > 0) score += 5m;
        return Math.Min(100m, score);
    }

    private static string BuildEvidenceSummary(FacultyPublicationCandidatePreviewSourceRow row)
    {
        return
            $"PATCH017B6 split-identity-aware preview only: Faculty '{row.FacultyDisplayName}' ({row.FacultyId}); " +
            $"AcceptedFacultyIdentityCount={row.AcceptedDecisionCount}; selected accepted author " +
            $"'{row.AuthorDisplayName ?? "(none)"}' ({row.AuthorOpenAlexId ?? "(none)"}); " +
            $"work '{row.WorkTitle ?? row.WorkOpenAlexId}'; SelectedAcceptedAuthorAppearsOnWork={row.WorkAuthorshipId.HasValue}; " +
            $"IsCorresponding={row.IsCorresponding}; AuthorshipInstitutionCount={row.AuthorshipInstitutionCount}; " +
            $"MatchingInstitutionCount={row.MatchingInstitutionCount}; ExistingCandidateId={(row.ExistingCandidateId?.ToString() ?? "(none)")}; " +
            $"ProposedMatchScore={row.ProposedMatchScore}; PreviewStatus={row.PreviewStatus}. " +
            $"Multiple accepted faculty identities are not treated as a conflict when one accepted identity is verified on this work. " +
            $"No FacultyPublicationCandidate row was inserted or updated.";
    }
}
