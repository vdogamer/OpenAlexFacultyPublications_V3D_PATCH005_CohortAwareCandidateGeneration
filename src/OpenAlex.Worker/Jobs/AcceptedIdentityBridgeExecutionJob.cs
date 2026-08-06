using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class AcceptedIdentityBridgeExecutionJob
{
    private readonly AcceptedIdentityBridgeExecutionRepository _repository;
    private readonly WorkIngestionRepository _ingestionRepository;
    private readonly IOperationalLogger _logger;
    private readonly AcceptedIdentityBridgeExecutionOptions _options;

    public AcceptedIdentityBridgeExecutionJob(
        AcceptedIdentityBridgeExecutionRepository repository,
        WorkIngestionRepository ingestionRepository,
        IOperationalLogger logger,
        IOptions<AcceptedIdentityBridgeExecutionOptions> options)
    {
        _repository = repository;
        _ingestionRepository = ingestionRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var previewRunId = _options.PreviewRunId
            ?? throw new InvalidOperationException(
                "AcceptedIdentityBridgeExecution:PreviewRunId is required. Use an explicit completed PATCH017B4 preview run ID.");

        if (!_options.ConfirmBridge)
        {
            throw new InvalidOperationException(
                "AcceptedIdentityBridgeExecution:ConfirmBridge must be true. The controlled bridge was not executed.");
        }

        if (_options.FacultyId <= 0)
        {
            throw new InvalidOperationException("AcceptedIdentityBridgeExecution:FacultyId must be a positive value.");
        }

        var maxIdentities = Math.Clamp(_options.MaxIdentities, 1, 5);
        var applicationVersion = FitRequired(_options.ApplicationVersion, 100, "ApplicationVersion");
        var ingestionApplicationVersion = FitRequired(_options.IngestionApplicationVersion, 50, "IngestionApplicationVersion");
        var startedBy = FitRequired(_options.StartedBy, 256, "StartedBy");
        var reviewedBy = FitRequired(_options.ReviewedBy, 256, "ReviewedBy");

        var safetyBefore = await _repository.GetSafetyCountsAsync(cancellationToken);
        var executionRunId = await _repository.StartRunAsync(
            previewRunId,
            _options.FacultyId,
            maxIdentities,
            safetyBefore,
            startedBy,
            applicationVersion,
            cancellationToken);

        long? ingestionRunId = null;
        var ingestionRunCompleted = false;

        await _logger.LogInfoAsync(
            "AcceptedIdentityBridgeExecution",
            "RunStarted",
            $"Started PATCH017B5 controlled bridge execution run {executionRunId}. PreviewRunId={previewRunId}; FacultyId={_options.FacultyId}; MaxIdentities={maxIdentities}.",
            cancellationToken: cancellationToken);

        try
        {
            var ingestionRun = await _ingestionRepository.StartRunAsync(
                "TestLoad",
                $"AcceptedIdentityBridgePreviewRunId:{previewRunId}",
                null,
                maxIdentities,
                ingestionApplicationVersion,
                startedBy,
                cancellationToken);

            ingestionRunId = ingestionRun.IngestionRunId;
            await _repository.AttachIngestionRunAsync(executionRunId, ingestionRunId.Value, cancellationToken);

            var result = await _repository.ExecuteBridgeAsync(
                executionRunId,
                previewRunId,
                _options.FacultyId,
                maxIdentities,
                ingestionRunId.Value,
                reviewedBy,
                cancellationToken);

            var safetyAfter = await _repository.GetSafetyCountsAsync(cancellationToken);
            var publicationSafetyUnchanged =
                safetyBefore.PublicationCandidateRows == safetyAfter.PublicationCandidateRows &&
                safetyBefore.PublicationDecisionRows == safetyAfter.PublicationDecisionRows;

            var summary =
                $"PATCH017B5 controlled accepted-identity bridge completed. PreviewRunId={previewRunId}; FacultyId={_options.FacultyId}; " +
                $"SelectedIdentities={result.SelectedIdentityCount}; CandidateRowsInserted={result.CandidateRowsInserted}; " +
                $"CandidateRowsUpdated={result.CandidateRowsUpdated}; DecisionRowsInserted={result.DecisionRowsInserted}; " +
                $"DecisionsAlreadyAccepted={result.DecisionsAlreadyAccepted}; MissingLocalAuthors={result.MissingLocalAuthorCount}; " +
                $"AuthorCandidateRowsBefore={safetyBefore.AuthorCandidateRows}; AuthorCandidateRowsAfter={safetyAfter.AuthorCandidateRows}; " +
                $"AuthorDecisionRowsBefore={safetyBefore.AuthorDecisionRows}; AuthorDecisionRowsAfter={safetyAfter.AuthorDecisionRows}; " +
                $"PublicationCandidateRowsBefore={safetyBefore.PublicationCandidateRows}; PublicationCandidateRowsAfter={safetyAfter.PublicationCandidateRows}; " +
                $"PublicationDecisionRowsBefore={safetyBefore.PublicationDecisionRows}; PublicationDecisionRowsAfter={safetyAfter.PublicationDecisionRows}; " +
                $"PublicationSafetyUnchanged={publicationSafetyUnchanged}. Publication candidate generation was not invoked.";

            await _ingestionRepository.CompleteRunAsync(
                ingestionRunId.Value,
                processed: result.SelectedIdentityCount,
                inserted: 0,
                updated: 0,
                authorshipRows: 0,
                authorshipInstitutionRows: 0,
                matchedInstitutionRows: 0,
                totalRequests: 0,
                successfulRequests: 0,
                failedRequests: 0,
                summaryText: summary,
                cancellationToken: cancellationToken);
            ingestionRunCompleted = true;

            await _repository.CompleteRunAsync(
                executionRunId,
                result,
                safetyAfter,
                summary,
                cancellationToken);

            await _logger.LogInfoAsync(
                "AcceptedIdentityBridgeExecution",
                "RunCompleted",
                summary,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            if (ingestionRunId.HasValue && !ingestionRunCompleted)
            {
                await _ingestionRepository.FailRunAsync(ingestionRunId.Value, ex, CancellationToken.None);
            }

            await _repository.FailRunAsync(executionRunId, ex, CancellationToken.None);
            await _logger.LogErrorAsync(
                "AcceptedIdentityBridgeExecution",
                "RunFailed",
                $"PATCH017B5 controlled bridge execution run {executionRunId} failed.",
                ex,
                cancellationToken: CancellationToken.None);
            throw;
        }
    }

    private static string FitRequired(string? value, int maxLength, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"AcceptedIdentityBridgeExecution:{optionName} is required.");
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
