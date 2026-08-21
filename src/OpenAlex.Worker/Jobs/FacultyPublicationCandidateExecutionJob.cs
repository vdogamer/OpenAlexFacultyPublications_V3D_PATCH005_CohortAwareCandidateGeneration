using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class FacultyPublicationCandidateExecutionJob
{
    private readonly FacultyPublicationCandidateExecutionRepository _repository;
    private readonly WorkIngestionRepository _ingestionRepository;
    private readonly IOperationalLogger _logger;
    private readonly FacultyPublicationCandidateExecutionOptions _options;

    public FacultyPublicationCandidateExecutionJob(
        FacultyPublicationCandidateExecutionRepository repository,
        WorkIngestionRepository ingestionRepository,
        IOperationalLogger logger,
        IOptions<FacultyPublicationCandidateExecutionOptions> options)
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
                "FacultyPublicationCandidateExecution:PreviewRunId is required. Use an explicit completed PATCH017B6 preview run ID.");

        if (!_options.ConfirmExecution)
        {
            throw new InvalidOperationException(
                "FacultyPublicationCandidateExecution:ConfirmExecution must be true. No publication candidates were created.");
        }

        if (_options.FacultyId <= 0)
        {
            throw new InvalidOperationException("FacultyPublicationCandidateExecution:FacultyId must be a positive value.");
        }

        var maxCandidates = Math.Clamp(_options.MaxCandidates, 1, 5);
        var startedBy = FitRequired(_options.StartedBy, 256, "StartedBy");
        var applicationVersion = FitRequired(_options.ApplicationVersion, 100, "ApplicationVersion");
        var ingestionApplicationVersion = FitRequired(_options.IngestionApplicationVersion, 50, "IngestionApplicationVersion");

        var safetyBefore = await _repository.GetSafetyCountsAsync(cancellationToken);
        var executionRunId = await _repository.StartRunAsync(
            previewRunId,
            _options.FacultyId,
            maxCandidates,
            safetyBefore,
            startedBy,
            applicationVersion,
            cancellationToken);

        long? ingestionRunId = null;
        var ingestionRunCompleted = false;

        await _logger.LogInfoAsync(
            "FacultyPublicationCandidateExecution",
            "RunStarted",
            $"Started PATCH017B7 controlled publication candidate execution run {executionRunId}. PreviewRunId={previewRunId}; FacultyId={_options.FacultyId}; MaxCandidates={maxCandidates}.",
            cancellationToken: cancellationToken);

        try
        {
            var ingestionRun = await _ingestionRepository.StartRunAsync(
                "TestLoad",
                $"FacultyPublicationCandidatePreviewRunId:{previewRunId}",
                null,
                maxCandidates,
                ingestionApplicationVersion,
                startedBy,
                cancellationToken);

            ingestionRunId = ingestionRun.IngestionRunId;
            await _repository.AttachIngestionRunAsync(executionRunId, ingestionRunId.Value, cancellationToken);

            var result = await _repository.ExecuteAsync(
                executionRunId,
                previewRunId,
                _options.FacultyId,
                maxCandidates,
                ingestionRunId.Value,
                cancellationToken);

            var safetyAfter = await _repository.GetSafetyCountsAsync(cancellationToken);
            var expectedCandidateRowsAfter = safetyBefore.PublicationCandidateRows + result.CandidateRowsInserted;
            var candidateCountExpected = safetyAfter.PublicationCandidateRows == expectedCandidateRowsAfter;
            var publicationDecisionSafetyUnchanged =
                safetyBefore.PublicationDecisionRows == safetyAfter.PublicationDecisionRows;

            var summary =
                $"PATCH017B7 controlled faculty-publication candidate execution completed. PreviewRunId={previewRunId}; FacultyId={_options.FacultyId}; " +
                $"SelectedCandidates={result.SelectedCandidateCount}; CandidateRowsInserted={result.CandidateRowsInserted}; " +
                $"CandidatesAlreadyExist={result.CandidatesAlreadyExist}; ValidationFailures={result.ValidationFailureCount}; " +
                $"PublicationCandidateRowsBefore={safetyBefore.PublicationCandidateRows}; PublicationCandidateRowsAfter={safetyAfter.PublicationCandidateRows}; " +
                $"ExpectedPublicationCandidateRowsAfter={expectedCandidateRowsAfter}; CandidateCountExpected={candidateCountExpected}; " +
                $"PublicationDecisionRowsBefore={safetyBefore.PublicationDecisionRows}; PublicationDecisionRowsAfter={safetyAfter.PublicationDecisionRows}; " +
                $"PublicationDecisionSafetyUnchanged={publicationDecisionSafetyUnchanged}. No FacultyPublicationDecision row was created or modified.";

            await _ingestionRepository.CompleteRunAsync(
                ingestionRunId.Value,
                processed: result.SelectedCandidateCount,
                inserted: result.CandidateRowsInserted,
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
                "FacultyPublicationCandidateExecution",
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
                "FacultyPublicationCandidateExecution",
                "RunFailed",
                $"PATCH017B7 controlled publication candidate execution run {executionRunId} failed.",
                ex,
                cancellationToken: CancellationToken.None);
            throw;
        }
    }

    private static string FitRequired(string? value, int maxLength, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"FacultyPublicationCandidateExecution:{optionName} is required.");
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
