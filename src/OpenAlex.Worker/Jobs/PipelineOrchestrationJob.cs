using Microsoft.Extensions.Options;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class PipelineOrchestrationJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOperationalLogger _logger;
    private readonly PipelineOrchestrationOptions _options;

    public PipelineOrchestrationJob(
        IServiceScopeFactory scopeFactory,
        IOperationalLogger logger,
        IOptions<PipelineOrchestrationOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();

        await _logger.LogInfoAsync(
            "Pipeline",
            "PipelineStarted",
            $"Starting integrated pipeline. Jobs={string.Join(", ", _options.JobSequence)}.",
            correlationId,
            cancellationToken);

        var completed = 0;
        var failed = 0;

        foreach (var jobName in _options.JobSequence)
        {
            if (string.IsNullOrWhiteSpace(jobName))
            {
                continue;
            }

            try
            {
                await _logger.LogInfoAsync(
                    "Pipeline",
                    "PipelineJobStarted",
                    $"Starting pipeline job '{jobName}'.",
                    correlationId,
                    cancellationToken);

                await ExecuteJobByNameAsync(jobName, cancellationToken);

                completed++;

                await _logger.LogInfoAsync(
                    "Pipeline",
                    "PipelineJobCompleted",
                    $"Completed pipeline job '{jobName}'.",
                    correlationId,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                failed++;

                await _logger.LogErrorAsync(
                    "Pipeline",
                    "PipelineJobFailed",
                    $"Pipeline job '{jobName}' failed.",
                    ex,
                    correlationId,
                    CancellationToken.None);

                if (_options.StopOnFirstFailure)
                {
                    throw;
                }
            }
        }

        await _logger.LogInfoAsync(
            "Pipeline",
            "PipelineCompleted",
            $"Integrated pipeline completed. CompletedJobs={completed}; FailedJobs={failed}.",
            correlationId,
            cancellationToken);
    }

    private async Task ExecuteJobByNameAsync(string jobName, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        if (string.Equals(jobName, "InstitutionIngestion", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<InstitutionIngestionJob>().ExecuteAsync(cancellationToken);
            return;
        }

        if (string.Equals(jobName, "AuthorIngestion", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<AuthorIngestionJob>().ExecuteAsync(cancellationToken);
            return;
        }

        if (string.Equals(jobName, "WorkIngestion", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<WorkIngestionJob>().ExecuteAsync(cancellationToken);
            return;
        }

        if (string.Equals(jobName, "FacultyIdentityCandidateGeneration", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyIdentityCandidateGenerationJob>().ExecuteAsync(cancellationToken);
            return;
        }

        if (string.Equals(jobName, "FacultyIdentityCandidateWorkExpansion", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyIdentityCandidateWorkExpansionJob>().ExecuteAsync(cancellationToken);
            return;
        }

        if (string.Equals(jobName, "FacultyIdentityWorkEvidenceScoring", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyIdentityWorkEvidenceScoringJob>().ExecuteAsync(cancellationToken);
            return;
        }

        if (string.Equals(jobName, "FacultyIdentityCandidateSummaryGeneration", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyIdentityCandidateSummaryGenerationJob>().ExecuteAsync(cancellationToken);
            return;
        }

        if (string.Equals(jobName, "FacultyAuthorCandidateGeneration", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyAuthorCandidateGenerationJob>().ExecuteAsync(cancellationToken);
            return;
        }

        if (string.Equals(jobName, "FacultyPublicationCandidateGeneration", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyPublicationCandidateGenerationJob>().ExecuteAsync(cancellationToken);
            return;
        }

        if (string.Equals(jobName, "InstitutionIngestionPreview", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<InstitutionIngestionPreviewJob>().ExecuteAsync(cancellationToken);
            return;
        }

        throw new InvalidOperationException($"Unknown pipeline job name: {jobName}");
    }
}
