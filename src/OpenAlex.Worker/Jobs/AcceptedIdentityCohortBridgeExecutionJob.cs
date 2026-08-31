using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class AcceptedIdentityCohortBridgeExecutionJob
{
    private readonly AcceptedIdentityCohortBridgeExecutionRepository _repository;
    private readonly CandidateGenerationRepository _candidateGenerationRepository;
    private readonly IOperationalLogger _logger;
    private readonly AcceptedIdentityCohortBridgeExecutionOptions _options;

    public AcceptedIdentityCohortBridgeExecutionJob(
        AcceptedIdentityCohortBridgeExecutionRepository repository,
        CandidateGenerationRepository candidateGenerationRepository,
        IOperationalLogger logger,
        IOptions<AcceptedIdentityCohortBridgeExecutionOptions> options)
    {
        _repository = repository;
        _candidateGenerationRepository = candidateGenerationRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!_options.ConfirmBridge)
        {
            throw new InvalidOperationException(
                "AcceptedIdentityCohortBridgeExecution:ConfirmBridge must be true.");
        }

        var cohortName = string.IsNullOrWhiteSpace(_options.CohortName)
            ? throw new InvalidOperationException(
                "AcceptedIdentityCohortBridgeExecution:CohortName is required.")
            : _options.CohortName.Trim();

        var expectedFacultyCount = Math.Max(1, _options.ExpectedFacultyCount);
        var maxIdentities = Math.Clamp(_options.MaxIdentities, 1, 100);
        var startedBy = FitRequired(_options.StartedBy, 256, "StartedBy");
        var reviewedBy = FitRequired(_options.ReviewedBy, 256, "ReviewedBy");
        var applicationVersion = FitRequired(_options.ApplicationVersion, 50, "ApplicationVersion");

        var safetyBefore = await _repository.GetSafetyCountsAsync(cancellationToken);
        var auditRunId = await _repository.StartAuditRunAsync(
            cohortName,
            expectedFacultyCount,
            maxIdentities,
            safetyBefore,
            startedBy,
            applicationVersion,
            cancellationToken);

        long? ingestionRunId = null;

        await _logger.LogInfoAsync(
            "AcceptedIdentityCohortBridgeExecution",
            "RunStarted",
            $"Started PATCH017B9 controlled cohort bridge audit run {auditRunId}. CohortName={cohortName}; ExpectedFacultyCount={expectedFacultyCount}; MaxIdentities={maxIdentities}.",
            cancellationToken: cancellationToken);

        try
        {
            var ingestionRun = await _candidateGenerationRepository.StartRunAsync(
                "TestLoad",
                $"AcceptedIdentityCohortBridgeExecution; PilotCohortName={cohortName}",
                null,
                maxIdentities,
                applicationVersion,
                startedBy,
                cancellationToken);
            ingestionRunId = ingestionRun.IngestionRunId;

            await _repository.AttachIngestionRunAsync(
                auditRunId,
                ingestionRunId.Value,
                cancellationToken);

            var result = await _repository.ExecuteBridgeAsync(
                cohortName,
                expectedFacultyCount,
                maxIdentities,
                ingestionRunId.Value,
                reviewedBy,
                cancellationToken);

            var safetyAfter = await _repository.GetSafetyCountsAsync(cancellationToken);
            var publicationSafetyUnchanged =
                safetyBefore.PublicationCandidateRows == safetyAfter.PublicationCandidateRows &&
                safetyBefore.PublicationDecisionRows == safetyAfter.PublicationDecisionRows;

            if (!publicationSafetyUnchanged)
            {
                throw new InvalidOperationException(
                    "Publication candidate/decision counts changed during the controlled identity bridge. Inspect concurrent activity before continuing.");
            }

            var summary =
                $"PATCH017B9 controlled five-faculty accepted-identity bridge completed. CohortName={cohortName}; " +
                $"EnabledFacultyCount={result.EnabledFacultyCount}; FacultyWithAcceptedIdentity={result.FacultyWithAcceptedIdentity}; " +
                $"SelectedAcceptedIdentities={result.SelectedAcceptedIdentityCount}; CandidateRowsInserted={result.CandidateRowsInserted}; " +
                $"CandidateRowsUpdated={result.CandidateRowsUpdated}; DecisionRowsInserted={result.DecisionRowsInserted}; " +
                $"DecisionsAlreadyAccepted={result.DecisionsAlreadyAccepted}; MissingLocalAuthors={result.MissingLocalAuthorCount}; " +
                $"AuthorCandidateRowsBefore={safetyBefore.AuthorCandidateRows}; AuthorCandidateRowsAfter={safetyAfter.AuthorCandidateRows}; " +
                $"AuthorDecisionRowsBefore={safetyBefore.AuthorDecisionRows}; AuthorDecisionRowsAfter={safetyAfter.AuthorDecisionRows}; " +
                $"PublicationCandidateRowsBefore={safetyBefore.PublicationCandidateRows}; PublicationCandidateRowsAfter={safetyAfter.PublicationCandidateRows}; " +
                $"PublicationDecisionRowsBefore={safetyBefore.PublicationDecisionRows}; PublicationDecisionRowsAfter={safetyAfter.PublicationDecisionRows}; " +
                $"PublicationSafetyUnchanged={publicationSafetyUnchanged}. Publication candidate generation was not invoked.";

            await _candidateGenerationRepository.CompleteRunAsync(
                ingestionRunId.Value,
                summary,
                authorsProcessed: result.SelectedAcceptedIdentityCount,
                worksProcessed: 0,
                cancellationToken);

            await _repository.CompleteAuditRunAsync(
                auditRunId,
                result,
                safetyAfter,
                summary,
                cancellationToken);

            await _logger.LogInfoAsync(
                "AcceptedIdentityCohortBridgeExecution",
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

            await _repository.FailAuditRunAsync(
                auditRunId,
                ex,
                CancellationToken.None);

            await _logger.LogErrorAsync(
                "AcceptedIdentityCohortBridgeExecution",
                "RunFailed",
                $"PATCH017B9 controlled cohort bridge audit run {auditRunId} failed.",
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
                $"AcceptedIdentityCohortBridgeExecution:{optionName} is required.");
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
