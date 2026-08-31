using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class FacultyPublicationCohortExecutionJob
{
    private readonly FacultyPublicationCohortExecutionRepository _repository;
    private readonly CandidateGenerationRepository _candidateGenerationRepository;
    private readonly IOperationalLogger _logger;
    private readonly FacultyPublicationCohortExecutionOptions _options;

    public FacultyPublicationCohortExecutionJob(
        FacultyPublicationCohortExecutionRepository repository,
        CandidateGenerationRepository candidateGenerationRepository,
        IOperationalLogger logger,
        IOptions<FacultyPublicationCohortExecutionOptions> options)
    {
        _repository = repository;
        _candidateGenerationRepository = candidateGenerationRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!_options.ConfirmExecution)
        {
            throw new InvalidOperationException(
                "FacultyPublicationCohortExecution:ConfirmExecution must be true.");
        }

        var cohortName = string.IsNullOrWhiteSpace(_options.CohortName)
            ? throw new InvalidOperationException(
                "FacultyPublicationCohortExecution:CohortName is required.")
            : _options.CohortName.Trim();

        var expected = new FacultyPublicationCohortExecutionOptionsSnapshot
        {
            CohortName = cohortName,
            ExpectedFacultyCount = Math.Max(1, _options.ExpectedFacultyCount),
            ExpectedAcceptedAuthorDecisionCount = Math.Max(1, _options.ExpectedAcceptedAuthorDecisionCount),
            ExpectedRawSourceRowCount = Math.Max(1, _options.ExpectedRawSourceRowCount),
            ExpectedDeduplicatedCandidateCount = Math.Max(1, _options.ExpectedDeduplicatedCandidateCount),
            ExpectedDuplicateRawRowsRemoved = Math.Max(0, _options.ExpectedDuplicateRawRowsRemoved),
            ExpectedExistingCandidateCount = Math.Max(0, _options.ExpectedExistingCandidateCount),
            MaxCandidates = Math.Clamp(_options.MaxCandidates, 1, 5000)
        };

        if (expected.ExpectedDeduplicatedCandidateCount > expected.MaxCandidates)
        {
            throw new InvalidOperationException(
                "ExpectedDeduplicatedCandidateCount exceeds MaxCandidates.");
        }

        var startedBy = FitRequired(_options.StartedBy, 256, "StartedBy");
        var applicationVersion = FitRequired(_options.ApplicationVersion, 50, "ApplicationVersion");

        var safetyBefore = await _repository.GetSafetyCountsAsync(cancellationToken);
        var executionRunId = await _repository.StartRunAsync(
            expected,
            safetyBefore,
            startedBy,
            applicationVersion,
            cancellationToken);

        long? ingestionRunId = null;

        await _logger.LogInfoAsync(
            "FacultyPublicationCohortExecution",
            "RunStarted",
            $"Started PATCH017B11 publication candidate execution run {executionRunId}. CohortName={cohortName}; ExpectedCandidates={expected.ExpectedDeduplicatedCandidateCount}.",
            cancellationToken: cancellationToken);

        try
        {
            var ingestionRun = await _candidateGenerationRepository.StartRunAsync(
                "TestLoad",
                $"FacultyPublicationCohortExecution; PilotCohortName={cohortName}",
                null,
                expected.MaxCandidates,
                applicationVersion,
                startedBy,
                cancellationToken);

            ingestionRunId = ingestionRun.IngestionRunId;
            await _repository.AttachIngestionRunAsync(
                executionRunId,
                ingestionRunId.Value,
                cancellationToken);

            var result = await _repository.ExecuteAsync(
                executionRunId,
                expected,
                ingestionRunId.Value,
                cancellationToken);

            var safetyAfter = await _repository.GetSafetyCountsAsync(cancellationToken);

            if (safetyAfter.PublicationCandidateRows !=
                    safetyBefore.PublicationCandidateRows + result.CandidateRowsInserted)
            {
                throw new InvalidOperationException(
                    "Publication candidate row count did not increase by the controlled insert count.");
            }

            if (safetyAfter.PublicationDecisionRows != safetyBefore.PublicationDecisionRows)
            {
                throw new InvalidOperationException(
                    "Publication decision count changed during PATCH017B11 execution.");
            }

            var summary =
                $"PATCH017B11 five-faculty publication candidate execution completed. CohortName={cohortName}; " +
                $"EnabledFacultyCount={result.EnabledFacultyCount}; AcceptedAuthorDecisionCount={result.AcceptedAuthorDecisionCount}; " +
                $"RawSourceRows={result.RawSourceRowCount}; SelectedCandidates={result.SelectedCandidateCount}; " +
                $"DuplicateRawRowsRemoved={result.DuplicateRawRowsRemoved}; ExistingCandidates={result.ExistingCandidateCount}; " +
                $"CandidatesInserted={result.CandidateRowsInserted}; ValidationFailures={result.ValidationFailureCount}; " +
                $"PublicationCandidateRowsBefore={safetyBefore.PublicationCandidateRows}; PublicationCandidateRowsAfter={safetyAfter.PublicationCandidateRows}; " +
                $"PublicationDecisionRowsBefore={safetyBefore.PublicationDecisionRows}; PublicationDecisionRowsAfter={safetyAfter.PublicationDecisionRows}; " +
                "All inserted candidates use CandidateSource=AcceptedAuthorMatch and CandidateStatus=PendingReview.";

            await _candidateGenerationRepository.CompleteRunAsync(
                ingestionRunId.Value,
                summary,
                authorsProcessed: result.AcceptedAuthorDecisionCount,
                worksProcessed: result.SelectedCandidateCount,
                cancellationToken);

            await _repository.CompleteRunAsync(
                executionRunId,
                result,
                safetyAfter,
                summary,
                cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyPublicationCohortExecution",
                "RunCompleted",
                summary,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            if (ingestionRunId.HasValue)
            {
                try
                {
                    await _candidateGenerationRepository.FailRunAsync(
                        ingestionRunId.Value,
                        ex,
                        CancellationToken.None);
                }
                catch
                {
                    // Preserve original failure.
                }
            }

            await _repository.FailRunAsync(
                executionRunId,
                ex,
                CancellationToken.None);

            await _logger.LogErrorAsync(
                "FacultyPublicationCohortExecution",
                "RunFailed",
                $"PATCH017B11 publication candidate execution run {executionRunId} failed.",
                ex,
                cancellationToken: CancellationToken.None);

            throw;
        }
    }

    private static string FitRequired(string? value, int maxLength, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"FacultyPublicationCohortExecution:{optionName} is required.");
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
