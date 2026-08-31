using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class AcceptedIdentityWorkPromotionJob
{
    private readonly AcceptedIdentityWorkPromotionRepository _repository;
    private readonly WorkIngestionRepository _workIngestionRepository;
    private readonly IOperationalLogger _logger;
    private readonly AcceptedIdentityWorkPromotionOptions _options;
    private readonly OpenAlexOptions _openAlexOptions;

    public AcceptedIdentityWorkPromotionJob(
        AcceptedIdentityWorkPromotionRepository repository,
        WorkIngestionRepository workIngestionRepository,
        IOperationalLogger logger,
        IOptions<AcceptedIdentityWorkPromotionOptions> options,
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
        if (!_options.ConfirmPromotion)
        {
            throw new InvalidOperationException(
                "AcceptedIdentityWorkPromotion:ConfirmPromotion must be true.");
        }

        var cohortName = string.IsNullOrWhiteSpace(_options.CohortName)
            ? throw new InvalidOperationException(
                "AcceptedIdentityWorkPromotion:CohortName is required.")
            : _options.CohortName.Trim();

        var expectedFacultyCount = Math.Max(1, _options.ExpectedFacultyCount);
        var expectedMissingWorkCount = Math.Max(0, _options.ExpectedMissingWorkCount);
        var maxWorks = Math.Clamp(_options.MaxWorks, 1, 5000);
        var startedBy = FitRequired(_options.StartedBy, 256, "StartedBy");
        var applicationVersion = FitRequired(_options.ApplicationVersion, 50, "ApplicationVersion");

        var safetyBefore = await _repository.GetSafetyCountsAsync(cancellationToken);

        var promotionRunId = await _repository.StartRunAsync(
            cohortName,
            expectedFacultyCount,
            expectedMissingWorkCount,
            maxWorks,
            safetyBefore,
            startedBy,
            applicationVersion,
            cancellationToken);

        long? ingestionRunId = null;

        await _logger.LogInfoAsync(
            "AcceptedIdentityWorkPromotion",
            "RunStarted",
            $"Started PATCH017B10A local work promotion run {promotionRunId}. CohortName={cohortName}; ExpectedMissingWorkCount={expectedMissingWorkCount}; MaxWorks={maxWorks}.",
            cancellationToken: cancellationToken);

        try
        {
            var sourceRows = await _repository.LoadSourceRowsAsync(
                cohortName,
                expectedFacultyCount,
                expectedMissingWorkCount,
                maxWorks,
                cancellationToken);

            if (sourceRows.Count != expectedMissingWorkCount)
            {
                throw new InvalidOperationException(
                    $"Selected work count {sourceRows.Count} did not equal ExpectedMissingWorkCount {expectedMissingWorkCount}.");
            }

            var ingestionRun = await _workIngestionRepository.StartRunAsync(
                "TestLoad",
                $"AcceptedIdentityWorkPromotion; PilotCohortName={cohortName}",
                null,
                maxWorks,
                applicationVersion,
                startedBy,
                cancellationToken);

            ingestionRunId = ingestionRun.IngestionRunId;
            await _repository.AttachIngestionRunAsync(
                promotionRunId,
                ingestionRunId.Value,
                cancellationToken);

            var result = new AcceptedIdentityWorkPromotionResult
            {
                SelectedWorkCount = sourceRows.Count
            };

            foreach (var source in sourceRows)
            {
                try
                {
                    using var document = JsonDocument.Parse(source.WorkRawJson);
                    var upsert = await _workIngestionRepository.UpsertWorkAsync(
                        document.RootElement,
                        ingestionRunId.Value,
                        _openAlexOptions.InstitutionIds,
                        cancellationToken);

                    if (upsert.Inserted) result.InsertedWorkCount++;
                    if (upsert.Updated) result.UpdatedWorkCount++;
                    result.AuthorshipRows += upsert.AuthorshipRows;
                    result.AuthorshipInstitutionRows += upsert.AuthorshipInstitutionRows;
                    result.MatchedInstitutionRows += upsert.MatchedInstitutionRows;

                    await _repository.SaveItemAsync(
                        promotionRunId,
                        source,
                        upsert.WorkId,
                        upsert.Inserted,
                        upsert.Updated,
                        upsert.AuthorshipRows,
                        upsert.AuthorshipInstitutionRows,
                        upsert.MatchedInstitutionRows,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    try
                    {
                        await _repository.SaveFailedItemAsync(
                            promotionRunId,
                            source,
                            ex,
                            CancellationToken.None);
                    }
                    catch
                    {
                        // Preserve original failure.
                    }

                    throw;
                }
            }

            var safetyAfter = await _repository.GetSafetyCountsAsync(cancellationToken);
            if (safetyBefore.PublicationCandidateRows != safetyAfter.PublicationCandidateRows ||
                safetyBefore.PublicationDecisionRows != safetyAfter.PublicationDecisionRows)
            {
                throw new InvalidOperationException(
                    "Publication candidate/decision counts changed during local work promotion.");
            }

            var expectedWorkRowsAfter = safetyBefore.WorkRows + result.InsertedWorkCount;
            if (safetyAfter.WorkRows != expectedWorkRowsAfter)
            {
                throw new InvalidOperationException(
                    $"Normalized work row count mismatch. Expected {expectedWorkRowsAfter}, observed {safetyAfter.WorkRows}.");
            }

            var summary =
                $"PATCH017B10A local accepted-identity work promotion completed. CohortName={cohortName}; " +
                $"SelectedWorks={result.SelectedWorkCount}; InsertedWorks={result.InsertedWorkCount}; UpdatedWorks={result.UpdatedWorkCount}; " +
                $"AuthorshipRows={result.AuthorshipRows}; AuthorshipInstitutionRows={result.AuthorshipInstitutionRows}; MatchedInstitutionRows={result.MatchedInstitutionRows}; " +
                $"WorkRowsBefore={safetyBefore.WorkRows}; WorkRowsAfter={safetyAfter.WorkRows}; " +
                $"PublicationCandidateRowsBefore={safetyBefore.PublicationCandidateRows}; PublicationCandidateRowsAfter={safetyAfter.PublicationCandidateRows}; " +
                $"PublicationDecisionRowsBefore={safetyBefore.PublicationDecisionRows}; PublicationDecisionRowsAfter={safetyAfter.PublicationDecisionRows}; " +
                "OpenAlexRequests=0; Source=FacultyIdentityCandidateWork.WorkRawJson.";

            await _workIngestionRepository.CompleteRunAsync(
                ingestionRunId.Value,
                processed: result.SelectedWorkCount,
                inserted: result.InsertedWorkCount,
                updated: result.UpdatedWorkCount,
                authorshipRows: result.AuthorshipRows,
                authorshipInstitutionRows: result.AuthorshipInstitutionRows,
                matchedInstitutionRows: result.MatchedInstitutionRows,
                totalRequests: 0,
                successfulRequests: 0,
                failedRequests: 0,
                summaryText: summary,
                cancellationToken: cancellationToken);

            await _repository.CompleteRunAsync(
                promotionRunId,
                result,
                safetyAfter,
                summary,
                cancellationToken);

            await _logger.LogInfoAsync(
                "AcceptedIdentityWorkPromotion",
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
                    await _workIngestionRepository.FailRunAsync(
                        ingestionRunId.Value,
                        ex,
                        CancellationToken.None);
                }
                catch
                {
                    // Preserve original failure.
                }
            }

            await _repository.FailRunAsync(
                promotionRunId,
                ex,
                CancellationToken.None);

            await _logger.LogErrorAsync(
                "AcceptedIdentityWorkPromotion",
                "RunFailed",
                $"PATCH017B10A local work promotion run {promotionRunId} failed.",
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
                $"AcceptedIdentityWorkPromotion:{optionName} is required.");
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
