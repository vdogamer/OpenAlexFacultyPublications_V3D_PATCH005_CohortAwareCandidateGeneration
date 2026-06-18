using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class InstitutionIngestionJob
{
    private readonly OpenAlexApiClient _client;
    private readonly InstitutionIngestionRepository _repository;
    private readonly IOperationalLogger _logger;
    private readonly InstitutionIngestionOptions _options;

    public InstitutionIngestionJob(
        OpenAlexApiClient client,
        InstitutionIngestionRepository repository,
        IOperationalLogger logger,
        IOptions<InstitutionIngestionOptions> options)
    {
        _client = client;
        _repository = repository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var filter = _client.BuildInstitutionIdFilter("id");

        var run = await _repository.StartRunAsync(
            _options.RunMode,
            filter,
            _options.MaxPages,
            _options.MaxRecords,
            _options.ApplicationVersion,
            _options.StartedBy,
            cancellationToken);

        await _logger.LogInfoAsync(
            "InstitutionIngestion",
            "RunStarted",
            $"Started institution ingestion run {run.IngestionRunId} with filter '{filter}'.",
            run.RunCorrelationId,
            cancellationToken);

        var processed = 0;
        var inserted = 0;
        var updated = 0;
        var totalRequests = 0;
        var successfulRequests = 0;
        var failedRequests = 0;

        try
        {
            var cursor = "*";
            var pageNumber = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                pageNumber++;

                var requestUri = _client.BuildCursorRequestUri("institutions", filter, cursor);
                var requestLogId = await _repository.StartRequestLogAsync(
                    run.IngestionRunId,
                    requestUri,
                    MaskMailTo(requestUri),
                    filter,
                    cursor,
                    pageNumber,
                    cancellationToken);

                totalRequests++;

                try
                {
                    using var page = await _client.GetInstitutionsPageAsync(filter, cursor, cancellationToken);

                    await _repository.CompleteRequestLogAsync(
                        requestLogId,
                        page.HttpStatusCode,
                        page.ResultsCount,
                        page.MetaCount,
                        page.MetaDbResponseTimeMs,
                        page.MetaPage,
                        page.MetaPerPage,
                        page.NextCursor,
                        page.ResponseContentLength,
                        cancellationToken);

                    successfulRequests++;

                    foreach (var item in page.ResultsElement.EnumerateArray())
                    {
                        if (_options.MaxRecords > 0 && processed >= _options.MaxRecords)
                        {
                            break;
                        }

                        var result = await _repository.UpsertInstitutionAsync(item, run.IngestionRunId, cancellationToken);

                        processed++;
                        if (result.Inserted)
                        {
                            inserted++;
                        }

                        if (result.Updated)
                        {
                            updated++;
                        }
                    }

                    if (_options.MaxPages > 0 && pageNumber >= _options.MaxPages)
                    {
                        break;
                    }

                    if (_options.MaxRecords > 0 && processed >= _options.MaxRecords)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(page.NextCursor) || page.NextCursor == cursor)
                    {
                        break;
                    }

                    cursor = page.NextCursor;
                }
                catch (Exception ex)
                {
                    failedRequests++;

                    await _repository.FailRequestLogAsync(requestLogId, ex, CancellationToken.None);

                    await _logger.LogErrorAsync(
                        "InstitutionIngestion",
                        "RequestFailed",
                        $"Institution ingestion request failed on page {pageNumber}.",
                        ex,
                        run.RunCorrelationId,
                        CancellationToken.None);

                    throw;
                }
            }

            var summaryText =
                $"Institution ingestion completed. Processed={processed}; Inserted={inserted}; Updated={updated}; " +
                $"Requests={totalRequests}; SuccessfulRequests={successfulRequests}; FailedRequests={failedRequests}.";

            await _repository.CompleteRunAsync(
                run.IngestionRunId,
                processed,
                inserted,
                updated,
                totalRequests,
                successfulRequests,
                failedRequests,
                summaryText,
                cancellationToken);

            await _logger.LogInfoAsync(
                "InstitutionIngestion",
                "RunCompleted",
                summaryText,
                run.RunCorrelationId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _repository.FailRunAsync(run.IngestionRunId, ex, CancellationToken.None);

            await _logger.LogErrorAsync(
                "InstitutionIngestion",
                "RunFailed",
                $"Institution ingestion run {run.IngestionRunId} failed.",
                ex,
                run.RunCorrelationId,
                CancellationToken.None);

            throw;
        }
    }


    private static string MaskMailTo(string requestUri)
    {
        return MaskQueryParameter(MaskQueryParameter(requestUri, "api_key"), "mailto");
    }

    private static string MaskQueryParameter(string requestUri, string parameterName)
    {
        var token = parameterName + "=";
        var index = requestUri.IndexOf(token, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            return requestUri;
        }

        var nextAmpersand = requestUri.IndexOf('&', index);

        return nextAmpersand < 0
            ? requestUri[..index] + parameterName + "=masked"
            : requestUri[..index] + parameterName + "=masked" + requestUri[nextAmpersand..];
    }

}
