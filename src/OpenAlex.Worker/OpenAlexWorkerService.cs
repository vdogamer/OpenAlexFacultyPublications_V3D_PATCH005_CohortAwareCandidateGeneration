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
            var job = scope.ServiceProvider.GetRequiredService<InstitutionIngestionPreviewJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        if (string.Equals(_options.Value.JobName, "InstitutionIngestion", StringComparison.OrdinalIgnoreCase))
        {
            var job = scope.ServiceProvider.GetRequiredService<InstitutionIngestionJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        if (string.Equals(_options.Value.JobName, "AuthorIngestion", StringComparison.OrdinalIgnoreCase))
        {
            var job = scope.ServiceProvider.GetRequiredService<AuthorIngestionJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        if (string.Equals(_options.Value.JobName, "WorkIngestion", StringComparison.OrdinalIgnoreCase))
        {
            var job = scope.ServiceProvider.GetRequiredService<WorkIngestionJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        if (string.Equals(_options.Value.JobName, "FacultyIdentityCandidateGeneration", StringComparison.OrdinalIgnoreCase))
        {
            var job = scope.ServiceProvider.GetRequiredService<FacultyIdentityCandidateGenerationJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        if (string.Equals(_options.Value.JobName, "FacultyIdentityCandidateWorkExpansion", StringComparison.OrdinalIgnoreCase))
        {
            var job = scope.ServiceProvider.GetRequiredService<FacultyIdentityCandidateWorkExpansionJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        if (string.Equals(_options.Value.JobName, "FacultyIdentityWorkEvidenceScoring", StringComparison.OrdinalIgnoreCase))
        {
            var job = scope.ServiceProvider.GetRequiredService<FacultyIdentityWorkEvidenceScoringJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        if (string.Equals(_options.Value.JobName, "FacultyIdentityCandidateSummaryGeneration", StringComparison.OrdinalIgnoreCase))
        {
            var job = scope.ServiceProvider.GetRequiredService<FacultyIdentityCandidateSummaryGenerationJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        if (string.Equals(_options.Value.JobName, "FacultyAuthorCandidateGeneration", StringComparison.OrdinalIgnoreCase))
        {
            var job = scope.ServiceProvider.GetRequiredService<FacultyAuthorCandidateGenerationJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        if (string.Equals(_options.Value.JobName, "FacultyPublicationCandidateGeneration", StringComparison.OrdinalIgnoreCase))
        {
            var job = scope.ServiceProvider.GetRequiredService<FacultyPublicationCandidateGenerationJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        if (string.Equals(_options.Value.JobName, "AcademicOnlineComparison", StringComparison.OrdinalIgnoreCase))
        {
            var job = scope.ServiceProvider.GetRequiredService<AcademicOnlineComparisonJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        if (string.Equals(_options.Value.JobName, "AcademicOnlineWorkBackfillPreview", StringComparison.OrdinalIgnoreCase))
        {
            var job = scope.ServiceProvider.GetRequiredService<AcademicOnlineWorkBackfillPreviewJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        if (string.Equals(_options.Value.JobName, "ProcessQueuedJobRequests", StringComparison.OrdinalIgnoreCase))
        {
            var job = scope.ServiceProvider.GetRequiredService<QueuedJobRequestProcessorJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        if (string.Equals(_options.Value.JobName, "RunPipelineOnce", StringComparison.OrdinalIgnoreCase))
        {
            var job = scope.ServiceProvider.GetRequiredService<PipelineOrchestrationJob>();
            await job.ExecuteAsync(stoppingToken);
            return;
        }

        await _logger.LogWarningAsync(
            "Worker",
            "UnknownWorkerJob",
            $"Unknown worker job name: {_options.Value.JobName}.",
            cancellationToken: stoppingToken);
    }
}
