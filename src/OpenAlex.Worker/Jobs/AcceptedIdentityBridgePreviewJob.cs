using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class AcceptedIdentityBridgePreviewJob
{
    private readonly AcceptedIdentityBridgePreviewRepository _repository;
    private readonly IOperationalLogger _logger;
    private readonly AcceptedIdentityBridgePreviewOptions _options;

    public AcceptedIdentityBridgePreviewJob(
        AcceptedIdentityBridgePreviewRepository repository,
        IOperationalLogger logger,
        IOptions<AcceptedIdentityBridgePreviewOptions> options)
    {
        _repository = repository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var sourcePreviewRunId = _options.SourceIdentityResolutionPreviewRunId
            ?? throw new InvalidOperationException(
                "AcceptedIdentityBridgePreview:SourceIdentityResolutionPreviewRunId is required. Use an explicit completed PATCH017B3 preview run ID.");

        var maxIdentities = Math.Clamp(_options.MaxIdentities, 1, 5);
        var safetyBefore = await _repository.GetSafetyCountsAsync(cancellationToken);
        var previewRunId = await _repository.StartRunAsync(
            sourcePreviewRunId,
            _options.FacultyId,
            maxIdentities,
            safetyBefore,
            _options.StartedBy,
            _options.ApplicationVersion,
            cancellationToken);

        await _logger.LogInfoAsync(
            "AcceptedIdentityBridgePreview",
            "RunStarted",
            $"Started PATCH017B4 accepted-identity bridge preview run {previewRunId}. SourceIdentityResolutionPreviewRunId={sourcePreviewRunId}; FacultyId={_options.FacultyId}; MaxIdentities={maxIdentities}.",
            cancellationToken: cancellationToken);

        try
        {
            var rows = await _repository.LoadSourceRowsAsync(
                sourcePreviewRunId,
                _options.FacultyId,
                maxIdentities,
                cancellationToken);

            var candidateInserts = 0;
            var candidateUpdates = 0;
            var decisionInserts = 0;
            var alreadyAccepted = 0;
            var missingSummary = 0;
            var missingLocalAuthor = 0;

            foreach (var row in rows)
            {
                Classify(row);

                if (row.FacultyIdentityCandidateSummaryId is null)
                {
                    missingSummary++;
                }
                else if (row.ExistingBridgeCandidateId.HasValue)
                {
                    candidateUpdates++;
                }
                else
                {
                    candidateInserts++;
                }

                if (row.ExistingAcceptedAuthorDecisionId.HasValue)
                {
                    alreadyAccepted++;
                }
                else if (row.FacultyIdentityCandidateSummaryId.HasValue)
                {
                    decisionInserts++;
                }

                if (!row.LocalAuthorId.HasValue)
                {
                    missingLocalAuthor++;
                }

                row.EvidenceSummary = BuildEvidenceSummary(row);
                row.EvidenceJson = JsonSerializer.Serialize(new
                {
                    row.SourceIdentityResolutionPreviewRunId,
                    row.FacultyIdentityCandidateDecisionId,
                    row.FacultyIdentityCandidateSummaryId,
                    row.FacultyId,
                    row.FacultyDisplayName,
                    row.CandidateAuthorOpenAlexId,
                    row.CandidateAuthorOpenAlexShortId,
                    row.CandidateDisplayName,
                    row.CandidateOrcid,
                    row.BestIdentityEvidenceScore,
                    row.LocalAuthorId,
                    row.ExistingBridgeCandidateId,
                    row.ExistingBridgeCandidateStatus,
                    row.ExistingBridgeCandidateMatchScore,
                    row.ExistingAcceptedAuthorDecisionId,
                    row.CurrentAuthorDecisionCount,
                    ProposedCandidateSource = "AcceptedIdentityDecision",
                    ProposedCandidateStatus = "AutoAcceptedCandidate",
                    ProposedAuthorDecisionStatus = "Accepted",
                    ProposedDecisionReason = "AcceptedIdentityDecision",
                    ProposedDecisionSource = "IdentityReviewBridge",
                    row.ProposedCandidateAction,
                    row.ProposedDecisionAction,
                    row.PreviewStatus,
                    row.ProposedNextAction,
                    PreviewOnly = true
                });

                await _repository.SaveItemAsync(previewRunId, row, cancellationToken);
            }

            var safetyAfter = await _repository.GetSafetyCountsAsync(cancellationToken);
            var safetyUnchanged =
                safetyBefore.AuthorCandidateRows == safetyAfter.AuthorCandidateRows &&
                safetyBefore.AuthorDecisionRows == safetyAfter.AuthorDecisionRows &&
                safetyBefore.PublicationCandidateRows == safetyAfter.PublicationCandidateRows &&
                safetyBefore.PublicationDecisionRows == safetyAfter.PublicationDecisionRows;

            var summary =
                $"PATCH017B4 accepted-identity bridge preview completed. SourceIdentityResolutionPreviewRunId={sourcePreviewRunId}; FacultyId={_options.FacultyId}; " +
                $"SelectedAcceptedIdentities={rows.Count}; CandidateInserts={candidateInserts}; CandidateUpdates={candidateUpdates}; " +
                $"DecisionInserts={decisionInserts}; AlreadyAcceptedDecisions={alreadyAccepted}; MissingIdentitySummaries={missingSummary}; MissingLocalAuthors={missingLocalAuthor}; " +
                $"AuthorCandidateRowsBefore={safetyBefore.AuthorCandidateRows}; AuthorCandidateRowsAfter={safetyAfter.AuthorCandidateRows}; " +
                $"AuthorDecisionRowsBefore={safetyBefore.AuthorDecisionRows}; AuthorDecisionRowsAfter={safetyAfter.AuthorDecisionRows}; " +
                $"PublicationCandidateRowsBefore={safetyBefore.PublicationCandidateRows}; PublicationCandidateRowsAfter={safetyAfter.PublicationCandidateRows}; " +
                $"PublicationDecisionRowsBefore={safetyBefore.PublicationDecisionRows}; PublicationDecisionRowsAfter={safetyAfter.PublicationDecisionRows}; " +
                $"SafetyUnchanged={safetyUnchanged}. AcceptedIdentityPublicationBridgeRepository.SyncAcceptedIdentitiesAsync was not invoked.";

            await _repository.CompleteRunAsync(
                previewRunId,
                rows.Count,
                candidateInserts,
                candidateUpdates,
                decisionInserts,
                alreadyAccepted,
                missingSummary,
                missingLocalAuthor,
                safetyAfter,
                summary,
                cancellationToken);

            await _logger.LogInfoAsync(
                "AcceptedIdentityBridgePreview",
                "RunCompleted",
                summary,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await _repository.FailRunAsync(previewRunId, ex, CancellationToken.None);
            await _logger.LogErrorAsync(
                "AcceptedIdentityBridgePreview",
                "RunFailed",
                $"PATCH017B4 accepted-identity bridge preview run {previewRunId} failed.",
                ex,
                cancellationToken: CancellationToken.None);
            throw;
        }
    }

    private static void Classify(AcceptedIdentityBridgePreviewSourceRow row)
    {
        if (!row.FacultyIdentityCandidateSummaryId.HasValue)
        {
            row.ProposedCandidateAction = "None";
            row.ProposedDecisionAction = "None";
            row.PreviewStatus = "BlockedMissingIdentitySummary";
            row.ProposedNextAction = "RepairIdentitySummaryBeforeBridge";
            return;
        }

        row.ProposedCandidateAction = row.ExistingBridgeCandidateId.HasValue
            ? "UpdateAcceptedIdentityCandidate"
            : "InsertAcceptedIdentityCandidate";

        row.ProposedDecisionAction = row.ExistingAcceptedAuthorDecisionId.HasValue
            ? "SkipAlreadyAcceptedDecision"
            : "InsertAcceptedAuthorDecision";

        if (row.ExistingBridgeCandidateId.HasValue && row.ExistingAcceptedAuthorDecisionId.HasValue)
        {
            row.PreviewStatus = "AlreadyBridged";
            row.ProposedNextAction = "RerunPATCH017B2CandidatePreview";
            return;
        }

        if (!row.LocalAuthorId.HasValue)
        {
            row.PreviewStatus = "ReadyToBridgeWithMissingLocalAuthorWarning";
            row.ProposedNextAction = "VerifyLocalAuthorCoverageThenRunControlledBridge";
            return;
        }

        if (row.ExistingBridgeCandidateId.HasValue)
        {
            row.PreviewStatus = "ReadyToRefreshCandidateAndCreateDecision";
            row.ProposedNextAction = "RunControlledAcceptedIdentityBridge";
            return;
        }

        if (row.ExistingAcceptedAuthorDecisionId.HasValue)
        {
            row.PreviewStatus = "ReadyToCreateBridgeCandidateOnly";
            row.ProposedNextAction = "RunControlledAcceptedIdentityBridge";
            return;
        }

        row.PreviewStatus = "ReadyToCreateCandidateAndDecision";
        row.ProposedNextAction = "RunControlledAcceptedIdentityBridge";
    }

    private static string BuildEvidenceSummary(AcceptedIdentityBridgePreviewSourceRow row)
    {
        return
            $"PATCH017B4 preview only: Faculty='{row.FacultyDisplayName}' ({row.FacultyId}); " +
            $"AcceptedIdentityDecisionId={row.FacultyIdentityCandidateDecisionId}; IdentitySummaryId={(row.FacultyIdentityCandidateSummaryId?.ToString() ?? "(none)")}; " +
            $"Author='{row.CandidateDisplayName ?? "(unknown)"}' ({row.CandidateAuthorOpenAlexId}); ORCID='{row.CandidateOrcid ?? "(none)"}'; " +
            $"IdentityScore={(row.BestIdentityEvidenceScore?.ToString() ?? "(none)")}; LocalAuthorId={(row.LocalAuthorId?.ToString() ?? "(none)")}; " +
            $"ExistingBridgeCandidateId={(row.ExistingBridgeCandidateId?.ToString() ?? "(none)")}; ExistingAcceptedAuthorDecisionId={(row.ExistingAcceptedAuthorDecisionId?.ToString() ?? "(none)")}; " +
            $"CandidateAction={row.ProposedCandidateAction}; DecisionAction={row.ProposedDecisionAction}; PreviewStatus={row.PreviewStatus}; NextAction={row.ProposedNextAction}. " +
            "No faculty-author candidate, author decision, publication candidate, or publication decision was inserted or updated.";
    }
}
