using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class WorkIngestionJob
{
    private readonly OpenAlexApiClient _client;
    private readonly WorkIngestionRepository _repository;
    private readonly IOperationalLogger _logger;
    private readonly WorkIngestionOptions _options;
    private readonly OpenAlexOptions _openAlexOptions;

    public WorkIngestionJob(
        OpenAlexApiClient client,
        WorkIngestionRepository repository,
        IOperationalLogger logger,
        IOptions<WorkIngestionOptions> options,
        IOptions<OpenAlexOptions> openAlexOptions)
    {
        _client = client;
        _repository = repository;
        _logger = logger;
        _options = options.Value;
        _openAlexOptions = openAlexOptions.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var filter = _client.BuildInstitutionIdFilter("authorships.institutions.id");

        var run = await _repository.StartRunAsync(
            _options.RunMode,
            filter,
            _options.MaxPages,
            _options.MaxRecords,
            _options.ApplicationVersion,
            _options.StartedBy,
            cancellationToken);

        await _logger.LogInfoAsync(
            "WorkIngestion",
            "RunStarted",
            $"Started work ingestion run {run.IngestionRunId} with filter '{filter}'.",
            run.RunCorrelationId,
            cancellationToken);

        var processed = 0;
        var inserted = 0;
        var updated = 0;
        var authorshipRows = 0;
        var authorshipInstitutionRows = 0;
        var matchedInstitutionRows = 0;
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

                var requestUri = _client.BuildCursorRequestUri("works", filter, cursor);
                var safeRequestUri = OpenAlexRequestUriRedactor.Redact(requestUri);
                var requestLogId = await _repository.StartRequestLogAsync(
                    run.IngestionRunId,
                    safeRequestUri,
                    safeRequestUri,
                    filter,
                    cursor,
                    pageNumber,
                    cancellationToken);

                totalRequests++;

                try
                {
                    using var page = await _client.GetWorksPageAsync(filter, cursor, cancellationToken);

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

                        var result = await _repository.UpsertWorkAsync(
                            item,
                            run.IngestionRunId,
                            _openAlexOptions.InstitutionIds,
                            cancellationToken);

                        processed++;

                        if (result.Inserted)
                        {
                            inserted++;
                        }

                        if (result.Updated)
                        {
                            updated++;
                        }

                        authorshipRows += result.AuthorshipRows;
                        authorshipInstitutionRows += result.AuthorshipInstitutionRows;
                        matchedInstitutionRows += result.MatchedInstitutionRows;
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
                        "WorkIngestion",
                        "RequestFailed",
                        $"Work ingestion request failed on page {pageNumber}.",
                        ex,
                        run.RunCorrelationId,
                        CancellationToken.None);

                    throw;
                }
            }

            var summaryText =
                $"Work ingestion completed. Processed={processed}; Inserted={inserted}; Updated={updated}; " +
                $"AuthorshipRows={authorshipRows}; AuthorshipInstitutionRows={authorshipInstitutionRows}; " +
                $"MatchedInstitutionRows={matchedInstitutionRows}; Requests={totalRequests}; " +
                $"SuccessfulRequests={successfulRequests}; FailedRequests={failedRequests}.";

            await _repository.CompleteRunAsync(
                run.IngestionRunId,
                processed,
                inserted,
                updated,
                authorshipRows,
                authorshipInstitutionRows,
                matchedInstitutionRows,
                totalRequests,
                successfulRequests,
                failedRequests,
                summaryText,
                cancellationToken);

            await _logger.LogInfoAsync(
                "WorkIngestion",
                "RunCompleted",
                summaryText,
                run.RunCorrelationId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _repository.FailRunAsync(run.IngestionRunId, ex, CancellationToken.None);

            await _logger.LogErrorAsync(
                "WorkIngestion",
                "RunFailed",
                $"Work ingestion run {run.IngestionRunId} failed.",
                ex,
                run.RunCorrelationId,
                CancellationToken.None);

            throw;
        }
    }
}
