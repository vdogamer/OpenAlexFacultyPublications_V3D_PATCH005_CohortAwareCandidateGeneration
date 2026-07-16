using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class AcademicOnlineWorkBackfillJob
{
    private readonly AcademicOnlineWorkBackfillRepository _repository;
    private readonly WorkIngestionRepository _workIngestionRepository;
    private readonly IOperationalLogger _logger;
    private readonly AcademicOnlineWorkBackfillOptions _options;
    private readonly OpenAlexOptions _openAlexOptions;

    public AcademicOnlineWorkBackfillJob(
        AcademicOnlineWorkBackfillRepository repository,
        WorkIngestionRepository workIngestionRepository,
        IOperationalLogger logger,
        IOptions<AcademicOnlineWorkBackfillOptions> options,
        IOptions<OpenAlexOptions> openAlexOptions)
    {
        _repository = repository;
        _workIngestionRepository = workIngestionRepository;
        _logger = logger;
        _options = options.Value;
        _openAlexOptions = openAlexOptions.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var previewRunId = _options.PreviewRunId
            ?? throw new InvalidOperationException("AcademicOnlineWorkBackfill:PreviewRunId is required. Use an explicit completed PATCH017B preview run ID.");

        var maxWorks = Math.Clamp(_options.MaxWorks, 1, 5);
        var sourceRows = await _repository.LoadReadyItemsAsync(previewRunId, maxWorks, cancellationToken);

        if (sourceRows.Count == 0)
        {
            throw new InvalidOperationException($"Preview run {previewRunId} has no ReadyToFetch items available for controlled backfill.");
        }

        var safetyBefore = await _repository.GetSafetyCountsAsync(cancellationToken);
        var backfillRunId = await _repository.StartRunAsync(
            previewRunId,
            maxWorks,
            safetyBefore,
            _options.StartedBy,
            _options.ApplicationVersion,
            cancellationToken);

        long? ingestionRunId = null;

        try
        {
            var ingestionRun = await _workIngestionRepository.StartRunAsync(
                "ControlledBackfill",
                $"AcademicOnlinePreviewRunId:{previewRunId}",
                0,
                maxWorks,
                _options.ApplicationVersion,
                _options.StartedBy,
                cancellationToken);

            ingestionRunId = ingestionRun.IngestionRunId;
            await _repository.AttachWorkIngestionRunAsync(backfillRunId, ingestionRun.IngestionRunId, cancellationToken);

            await _logger.LogInfoAsync(
                "AcademicOnlineWorkBackfill",
                "RunStarted",
                $"Started PATCH017B1 controlled work backfill run {backfillRunId} from explicit preview run {previewRunId}. MaxWorks={maxWorks}.",
                ingestionRun.RunCorrelationId,
                cancellationToken);

            var inserted = 0;
            var alreadyExists = 0;
            var invalid = 0;
            var failed = 0;
            var updatedUnexpectedly = 0;
            var authorshipRows = 0;
            var authorshipInstitutionRows = 0;
            var matchedInstitutionRows = 0;

            foreach (var source in sourceRows)
            {
                if (string.IsNullOrWhiteSpace(source.NormalizedOpenAlexId))
                {
                    invalid++;
                    await _repository.SaveItemAsync(
                        backfillRunId, source, "InvalidPreviewItem", null, null, false,
                        0, 0, 0, "The ReadyToFetch preview item did not contain a normalized OpenAlex work ID.", cancellationToken);
                    continue;
                }

                var localWorkIdBefore = await _repository.GetLocalWorkIdAsync(source.NormalizedOpenAlexId, cancellationToken);
                if (localWorkIdBefore.HasValue)
                {
                    alreadyExists++;
                    await _repository.SaveItemAsync(
                        backfillRunId, source, "SkippedAlreadyExists", localWorkIdBefore, localWorkIdBefore, false,
                        0, 0, 0, "The work existed locally when PATCH017B1 rechecked immediately before insertion.", cancellationToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(source.RawResponseJson))
                {
                    invalid++;
                    await _repository.SaveItemAsync(
                        backfillRunId, source, "InvalidPreviewPayload", null, null, false,
                        0, 0, 0, "The ReadyToFetch preview item did not contain the canonical OpenAlex response JSON.", cancellationToken);
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(source.RawResponseJson);
                    var root = document.RootElement;
                    var returnedOpenAlexId = GetString(root, "id");
                    var returnedShortId = NormalizeOpenAlexId(returnedOpenAlexId);

                    if (root.ValueKind != JsonValueKind.Object ||
                        !string.Equals(returnedShortId, source.NormalizedOpenAlexId, StringComparison.OrdinalIgnoreCase))
                    {
                        invalid++;
                        await _repository.SaveItemAsync(
                            backfillRunId, source, "PreviewIdMismatch", null, null, false,
                            0, 0, 0,
                            $"Preview payload returned OpenAlex ID '{returnedOpenAlexId}', but the selected normalized ID was '{source.NormalizedOpenAlexId}'.",
                            cancellationToken);
                        continue;
                    }

                    var result = await _workIngestionRepository.UpsertWorkAsync(
                        root,
                        ingestionRun.IngestionRunId,
                        _openAlexOptions.InstitutionIds,
                        cancellationToken);

                    authorshipRows += result.AuthorshipRows;
                    authorshipInstitutionRows += result.AuthorshipInstitutionRows;
                    matchedInstitutionRows += result.MatchedInstitutionRows;

                    if (result.Inserted)
                    {
                        inserted++;
                        await _repository.SaveItemAsync(
                            backfillRunId, source, "Inserted", null, result.WorkId, true,
                            result.AuthorshipRows, result.AuthorshipInstitutionRows, result.MatchedInstitutionRows,
                            null, cancellationToken);
                    }
                    else
                    {
                        updatedUnexpectedly++;
                        alreadyExists++;
                        await _repository.SaveItemAsync(
                            backfillRunId, source, "ConcurrentExistingWorkUpdated", null, result.WorkId, false,
                            result.AuthorshipRows, result.AuthorshipInstitutionRows, result.MatchedInstitutionRows,
                            "The immediate pre-insert check found no local work, but the existing upsert path encountered an existing row.", cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    await _repository.SaveItemAsync(
                        backfillRunId, source, "InsertFailed", null, null, false,
                        0, 0, 0, ex.ToString(), CancellationToken.None);
                }
            }

            var safetyAfter = await _repository.GetSafetyCountsAsync(cancellationToken);
            var safetyUnchanged =
                safetyBefore.CandidateRows == safetyAfter.CandidateRows &&
                safetyBefore.DecisionRows == safetyAfter.DecisionRows;

            var summary =
                $"PATCH017B1 controlled work backfill completed. SourcePreviewRunId={previewRunId}; Selected={sourceRows.Count}; " +
                $"Inserted={inserted}; AlreadyExists={alreadyExists}; Invalid={invalid}; Failed={failed}; " +
                $"UnexpectedUpdates={updatedUnexpectedly}; AuthorshipRows={authorshipRows}; " +
                $"AuthorshipInstitutionRows={authorshipInstitutionRows}; MatchedInstitutionRows={matchedInstitutionRows}; " +
                $"CandidateRowsBefore={safetyBefore.CandidateRows}; CandidateRowsAfter={safetyAfter.CandidateRows}; " +
                $"DecisionRowsBefore={safetyBefore.DecisionRows}; DecisionRowsAfter={safetyAfter.DecisionRows}; " +
                $"CandidateAndDecisionSafetyUnchanged={safetyUnchanged}. Candidate generation and decision updates were not invoked.";

            await _workIngestionRepository.CompleteRunAsync(
                ingestionRun.IngestionRunId,
                inserted + updatedUnexpectedly,
                inserted,
                updatedUnexpectedly,
                authorshipRows,
                authorshipInstitutionRows,
                matchedInstitutionRows,
                0,
                0,
                failed,
                summary,
                cancellationToken);

            await _repository.CompleteRunAsync(
                backfillRunId,
                sourceRows.Count,
                inserted,
                alreadyExists,
                invalid,
                failed,
                authorshipRows,
                authorshipInstitutionRows,
                matchedInstitutionRows,
                safetyAfter,
                summary,
                cancellationToken);

            await _logger.LogInfoAsync(
                "AcademicOnlineWorkBackfill",
                "RunCompleted",
                summary,
                ingestionRun.RunCorrelationId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            if (ingestionRunId.HasValue)
            {
                await _workIngestionRepository.FailRunAsync(ingestionRunId.Value, ex, CancellationToken.None);
            }

            await _repository.FailRunAsync(backfillRunId, ex, CancellationToken.None);
            await _logger.LogErrorAsync(
                "AcademicOnlineWorkBackfill",
                "RunFailed",
                $"PATCH017B1 controlled work backfill run {backfillRunId} failed.",
                ex,
                cancellationToken: CancellationToken.None);
            throw;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? NormalizeOpenAlexId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            trimmed = uri.Segments.LastOrDefault()?.Trim('/') ?? trimmed;
        }

        return trimmed.ToUpperInvariant();
    }
}
