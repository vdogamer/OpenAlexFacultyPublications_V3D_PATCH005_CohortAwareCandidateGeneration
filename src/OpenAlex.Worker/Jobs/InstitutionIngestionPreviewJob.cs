using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class InstitutionIngestionPreviewJob
{
    private readonly OpenAlexApiClient _client;
    private readonly IOperationalLogger _logger;

    public InstitutionIngestionPreviewJob(OpenAlexApiClient client, IOperationalLogger logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();

        await _logger.LogInfoAsync(
            "Worker",
            "InstitutionIngestionPreviewStarted",
            "Starting safe OpenAlex institution ingestion preview. This job does not write institution rows.",
            correlationId,
            cancellationToken);

        var filter = _client.BuildInstitutionIdFilter("id");
        var page = await _client.GetInstitutionsPageAsync(filter, "*", cancellationToken);

        await _logger.LogInfoAsync(
            "Worker",
            "InstitutionIngestionPreviewCompleted",
            $"OpenAlex institution preview completed. Results on first page: {page.ResultsCount}. Next cursor available: {!string.IsNullOrWhiteSpace(page.NextCursor)}.",
            correlationId,
            cancellationToken);
    }
}
