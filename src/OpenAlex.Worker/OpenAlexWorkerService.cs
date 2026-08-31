using Microsoft.Extensions.Options;
using OpenAlex.OperationalLogging;
using OpenAlex.Worker.Jobs;

namespace OpenAlex.Worker;

public sealed class OpenAlexWorkerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<WorkerOptions> _options;
    private readonly IOperationalLogger _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public OpenAlexWorkerService(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerOptions> options,
        IOperationalLogger logger,
        IHostApplicationLifetime lifetime)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            await _logger.LogInfoAsync(
                "Worker",
                "WorkerDisabled",
                "OpenAlex integrated worker started, but Worker:Enabled is false.",
                cancellationToken: stoppingToken);
            return;
        }

        await _logger.LogInfoAsync(
            "Worker",
            "WorkerStarted",
            $"OpenAlex integrated worker started. JobName={_options.Value.JobName}.",
            cancellationToken: stoppingToken);

        do
        {
            try
            {
                await ExecuteConfiguredJobAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(
                    "Worker",
                    "WorkerJobFailed",
                    "Configured worker job failed.",
                    ex,
                    cancellationToken: CancellationToken.None);
            }

            if (_options.Value.RunOnceAndStop)
            {
                _lifetime.StopApplication();
                return;
            }

            var delay = TimeSpan.FromSeconds(Math.Max(10, _options.Value.DelaySeconds));
            await Task.Delay(delay, stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested);
    }

    private async Task ExecuteConfiguredJobAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        if (string.Equals(_options.Value.JobName, "InstitutionIngestionPreview", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<InstitutionIngestionPreviewJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "InstitutionIngestion", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<InstitutionIngestionJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "AuthorIngestion", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<AuthorIngestionJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "WorkIngestion", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<WorkIngestionJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "FacultyIdentityCandidateGeneration", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyIdentityCandidateGenerationJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "FacultyIdentityCandidateWorkExpansion", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyIdentityCandidateWorkExpansionJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "FacultyIdentityWorkEvidenceScoring", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyIdentityWorkEvidenceScoringJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "FacultyIdentityCandidateSummaryGeneration", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyIdentityCandidateSummaryGenerationJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "FacultyAuthorCandidateGeneration", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyAuthorCandidateGenerationJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "FacultyPublicationCandidateGeneration", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyPublicationCandidateGenerationJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "AcademicOnlineComparison", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<AcademicOnlineComparisonJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "AcademicOnlineWorkBackfillPreview", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<AcademicOnlineWorkBackfillPreviewJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "AcademicOnlineWorkBackfill", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<AcademicOnlineWorkBackfillJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "FacultyPublicationCandidatePreview", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyPublicationCandidatePreviewJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "FacultyPublicationCandidateExecution", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyPublicationCandidateExecutionJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "FacultyAuthorIdentityResolutionPreview", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyAuthorIdentityResolutionPreviewJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "AcceptedIdentityBridgePreview", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<AcceptedIdentityBridgePreviewJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "AcceptedIdentityBridgeExecution", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<AcceptedIdentityBridgeExecutionJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "AcceptedIdentityCohortBridgeExecution", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<AcceptedIdentityCohortBridgeExecutionJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "AcceptedIdentityWorkPromotion", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<AcceptedIdentityWorkPromotionJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "FacultyPublicationCohortExecution", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<FacultyPublicationCohortExecutionJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "ProcessQueuedJobRequests", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<QueuedJobRequestProcessorJob>().ExecuteAsync(stoppingToken);
            return;
        }
        if (string.Equals(_options.Value.JobName, "RunPipelineOnce", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<PipelineOrchestrationJob>().ExecuteAsync(stoppingToken);
            return;
        }

        await _logger.LogWarningAsync(
            "Worker",
            "UnknownWorkerJob",
            $"Unknown worker job name: {_options.Value.JobName}.",
            cancellationToken: stoppingToken);
    }
}
