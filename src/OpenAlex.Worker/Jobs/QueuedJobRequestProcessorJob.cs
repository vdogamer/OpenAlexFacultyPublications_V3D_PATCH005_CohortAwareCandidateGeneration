using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class QueuedJobRequestProcessorJob
{
    private readonly SchedulerRepository _schedulerRepository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOperationalLogger _logger;
    private readonly QueuedJobProcessorOptions _options;

    public QueuedJobRequestProcessorJob(
        SchedulerRepository schedulerRepository,
        IServiceScopeFactory scopeFactory,
        IOperationalLogger logger,
        IOptions<QueuedJobProcessorOptions> options)
    {
        _schedulerRepository = schedulerRepository;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var processed = 0;

        await _logger.LogInfoAsync(
            "QueuedJobProcessor",
            "ProcessorStarted",
            $"Queued job processor started. MaxRequestsPerRun={_options.MaxRequestsPerRun}.",
            cancellationToken: cancellationToken);

        while (!cancellationToken.IsCancellationRequested &&
               (_options.MaxRequestsPerRun <= 0 || processed < _options.MaxRequestsPerRun))
        {
            var request = await _schedulerRepository.ClaimNextQueuedRequestAsync(
                _options.WorkerApplicationVersion,
                cancellationToken);

            if (request is null)
            {
                await _logger.LogInfoAsync(
                    "QueuedJobProcessor",
                    "NoQueuedRequests",
                    "No queued job execution requests were found.",
                    cancellationToken: cancellationToken);
                break;
            }

            try
            {
                await _schedulerRepository.AddEventAsync(
                    request.JobExecutionRequestId,
                    "WorkerDispatchStarted",
                    "Information",
                    $"Dispatching job '{request.JobName}'.",
                    cancellationToken);

                await ExecuteJobByNameAsync(request.JobName, cancellationToken);

                await _schedulerRepository.MarkRequestCompletedAsync(
                    request.JobExecutionRequestId,
                    $"Job '{request.JobName}' completed successfully.",
                    cancellationToken);

                await _logger.LogInfoAsync(
                    "QueuedJobProcessor",
                    "RequestCompleted",
                    $"Completed queued request {request.JobExecutionRequestId} for job '{request.JobName}'.",
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _schedulerRepository.MarkRequestFailedAsync(request.JobExecutionRequestId, ex, CancellationToken.None);

                await _logger.LogErrorAsync(
                    "QueuedJobProcessor",
                    "RequestFailed",
                    $"Queued request {request.JobExecutionRequestId} for job '{request.JobName}' failed.",
                    ex,
                    cancellationToken: CancellationToken.None);

                if (_options.StopOnFirstFailure) throw;
            }

            processed++;
        }

        await _logger.LogInfoAsync(
            "QueuedJobProcessor",
            "ProcessorCompleted",
            $"Queued job processor completed. Processed={processed}.",
            cancellationToken: cancellationToken);
    }

    private async Task ExecuteJobByNameAsync(string jobName, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        if (string.Equals(jobName, "InstitutionIngestionPreview", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<InstitutionIngestionPreviewJob>().ExecuteAsync(cancellationToken);
            return;
        }
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
        if (string.Equals(jobName, "RunPipelineOnce", StringComparison.OrdinalIgnoreCase))
        {
            await scope.ServiceProvider.GetRequiredService<PipelineOrchestrationJob>().ExecuteAsync(cancellationToken);
            return;
        }
        throw new InvalidOperationException($"Unknown queued job name: {jobName}");
    }
}
