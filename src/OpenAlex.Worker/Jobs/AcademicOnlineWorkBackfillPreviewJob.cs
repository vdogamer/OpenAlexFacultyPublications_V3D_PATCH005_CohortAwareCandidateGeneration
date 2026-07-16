using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class AcademicOnlineWorkBackfillPreviewJob
{
    private readonly AcademicOnlineWorkBackfillPreviewRepository _repository;
    private readonly OpenAlexApiClient _client;
    private readonly IOperationalLogger _logger;
    private readonly AcademicOnlineWorkBackfillPreviewOptions _options;

    public AcademicOnlineWorkBackfillPreviewJob(
        AcademicOnlineWorkBackfillPreviewRepository repository,
        OpenAlexApiClient client,
        IOperationalLogger logger,
        IOptions<AcademicOnlineWorkBackfillPreviewOptions> options)
    {
        _repository = repository;
        _client = client;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var comparisonRunId = _options.AcademicOnlineComparisonRunId
            ?? await _repository.GetLatestAcademicOnlineComparisonRunIdAsync(cancellationToken)
            ?? throw new InvalidOperationException("No completed Academic Online comparison run was found.");

        var maxWorks = Math.Clamp(_options.MaxWorks, 1, 25);
        var maxSourceRows = Math.Clamp(_options.MaxSourceRows, maxWorks, 5000);

        var sourceRows = await _repository.LoadSourceRowsAsync(
            comparisonRunId,
            _options.FacultyId,
            maxSourceRows,
            cancellationToken);

        var selectedRows = sourceRows.Take(maxWorks).ToList();
        var previewRunId = await _repository.StartRunAsync(
            comparisonRunId,
            _options.FacultyId,
            maxWorks,
            _options.StartedBy,
            _options.ApplicationVersion,
            cancellationToken);

        var ready = 0;
        var alreadyExists = 0;
        var missingId = 0;
        var notFound = 0;
        var failed = 0;
        var duplicatesSkipped = selectedRows.Sum(row => Math.Max(0, row.SnapshotDuplicateCount - 1));

        try
        {
            foreach (var source in selectedRows)
            {
                if (source.LocalWorkId.HasValue)
                {
                    alreadyExists++;
                    await _repository.SaveItemAsync(
                        previewRunId, source, "AlreadyExistsLocally", null, null, null,
                        null, null, null, null, null, null, null,
                        "The work already existed locally when the preview selected the source row.", cancellationToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(source.NormalizedOpenAlexId))
                {
                    missingId++;
                    await _repository.SaveItemAsync(
                        previewRunId, source, "MissingOpenAlexId", null, null, null,
                        null, null, null, null, null, null, null,
                        "Academic Online did not provide an OpenAlex work ID for this publication.", cancellationToken);
                    continue;
                }

                try
                {
                    using var response = await _client.GetWorkByIdAsync(source.NormalizedOpenAlexId, cancellationToken);
                    var requestUriMasked = MaskSensitiveQueryParameters(response.RequestUri);
                    var rawJson = response.RawDocument?.RootElement.GetRawText();

                    if (response.HttpStatusCode == 404)
                    {
                        notFound++;
                        await _repository.SaveItemAsync(
                            previewRunId, source, "NotFoundByOpenAlex", requestUriMasked,
                            response.HttpStatusCode, response.ResponseContentLength,
                            null, null, null, null, null, null, rawJson,
                            response.ErrorContent ?? "OpenAlex returned HTTP 404 for the requested work ID.", cancellationToken);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode || response.RawDocument is null)
                    {
                        failed++;
                        await _repository.SaveItemAsync(
                            previewRunId, source, "FetchFailed", requestUriMasked,
                            response.HttpStatusCode, response.ResponseContentLength,
                            null, null, null, null, null, null, rawJson,
                            response.ErrorContent ?? "OpenAlex returned an unsuccessful or unreadable response.", cancellationToken);
                        continue;
                    }

                    var root = response.RawDocument.RootElement;
                    var returnedId = GetString(root, "id");
                    var returnedShortId = NormalizeOpenAlexId(returnedId);

                    if (!string.Equals(returnedShortId, source.NormalizedOpenAlexId, StringComparison.OrdinalIgnoreCase))
                    {
                        failed++;
                        await _repository.SaveItemAsync(
                            previewRunId, source, "FetchFailed", requestUriMasked,
                            response.HttpStatusCode, response.ResponseContentLength,
                            returnedId, GetString(root, "title") ?? GetString(root, "display_name"), GetString(root, "doi"),
                            GetInt(root, "publication_year"), GetString(root, "type"), GetInt(root, "cited_by_count"), rawJson,
                            $"OpenAlex returned work '{returnedId}', which did not match requested ID '{source.NormalizedOpenAlexId}'.", cancellationToken);
                        continue;
                    }

                    ready++;
                    await _repository.SaveItemAsync(
                        previewRunId, source, "ReadyToFetch", requestUriMasked,
                        response.HttpStatusCode, response.ResponseContentLength,
                        returnedId, GetString(root, "title") ?? GetString(root, "display_name"), GetString(root, "doi"),
                        GetInt(root, "publication_year"), GetString(root, "type"), GetInt(root, "cited_by_count"), rawJson,
                        null, cancellationToken);
                }
                catch (Exception ex)
                {
                    failed++;
                    await _repository.SaveItemAsync(
                        previewRunId, source, "FetchFailed", null, null, null,
                        null, null, null, null, null, null, null, ex.ToString(), CancellationToken.None);
                }
            }

            var summary =
                $"PATCH017B preview completed. Selected={selectedRows.Count}; ReadyToFetch={ready}; " +
                $"AlreadyExistsLocally={alreadyExists}; MissingOpenAlexId={missingId}; " +
                $"NotFoundByOpenAlex={notFound}; FetchFailed={failed}; " +
                $"DuplicateSnapshotRecordsSkipped={duplicatesSkipped}. No openalex.Work rows were inserted.";

            await _repository.CompleteRunAsync(
                previewRunId, selectedRows.Count, ready, alreadyExists, missingId, notFound, failed,
                duplicatesSkipped, summary, cancellationToken);

            await _logger.LogInfoAsync(
                "AcademicOnlineWorkBackfillPreview",
                "RunCompleted",
                summary,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await _repository.FailRunAsync(previewRunId, ex, CancellationToken.None);
            await _logger.LogErrorAsync(
                "AcademicOnlineWorkBackfillPreview",
                "RunFailed",
                $"PATCH017B preview run {previewRunId} failed.",
                ex,
                cancellationToken: CancellationToken.None);
            throw;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static string? NormalizeOpenAlexId(string? openAlexId)
    {
        if (string.IsNullOrWhiteSpace(openAlexId))
        {
            return null;
        }

        return openAlexId.Trim()
            .Replace("https://openalex.org/", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://openalex.org/", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim('/')
            .ToUpperInvariant();
    }

    private static string MaskSensitiveQueryParameters(string requestUri)
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
