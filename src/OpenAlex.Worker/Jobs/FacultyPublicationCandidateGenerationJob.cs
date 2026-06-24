using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class FacultyPublicationCandidateGenerationJob
{
    private readonly CandidateGenerationRepository _repository;
    private readonly AcceptedIdentityPublicationBridgeRepository _acceptedIdentityBridgeRepository;
    private readonly PublicationEvidenceEnhancementRepository _publicationEvidenceEnhancementRepository;
    private readonly IOperationalLogger _logger;
    private readonly CandidateGenerationOptions _options;

    public FacultyPublicationCandidateGenerationJob(
        CandidateGenerationRepository repository,
        AcceptedIdentityPublicationBridgeRepository acceptedIdentityBridgeRepository,
        PublicationEvidenceEnhancementRepository publicationEvidenceEnhancementRepository,
        IOperationalLogger logger,
        IOptions<CandidateGenerationOptions> options)
    {
        _repository = repository;
        _acceptedIdentityBridgeRepository = acceptedIdentityBridgeRepository;
        _publicationEvidenceEnhancementRepository = publicationEvidenceEnhancementRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var selectedCohortName = NormalizeCohortName(_options.PilotCohortName);

        var run = await _repository.StartRunAsync(
            _options.RunMode,
            (selectedCohortName is null
                ? "FacultyPublicationCandidateGeneration"
                : $"FacultyPublicationCandidateGeneration; PilotCohortName={selectedCohortName}"),
            null,
            _options.MaxPublicationCandidatesToGenerate,
            _options.ApplicationVersion,
            _options.StartedBy,
            cancellationToken);

        await _logger.LogInfoAsync(
            "FacultyPublicationCandidateGeneration",
            "RunStarted",
            $"Started faculty-publication candidate generation run {run.IngestionRunId}. PilotCohortName={selectedCohortName ?? "(none)"}.",
            run.RunCorrelationId,
            cancellationToken);

        try
        {
            IReadOnlyList<FacultyCandidateSource> cohortFaculties = selectedCohortName is null
                ? Array.Empty<FacultyCandidateSource>()
                : await _repository.LoadFacultyForPilotCohortAsync(selectedCohortName, _options.MaxFacultyToProcess, cancellationToken);

            var selectedFacultyIds = cohortFaculties.Select(f => f.FacultyId).ToList();

            if (selectedCohortName is not null)
            {
                await _logger.LogInfoAsync(
                    "FacultyPublicationCandidateGeneration",
                    "CohortSelectionDiagnostics",
                    $"SelectedCohortName={selectedCohortName}; EnabledCohortFacultyCount={cohortFaculties.Count}; FacultyIdsSelected={FormatIdList(selectedFacultyIds)}; MaxFacultyToProcess={_options.MaxFacultyToProcess}.",
                    run.RunCorrelationId,
                    cancellationToken);
            }

            var bridgeResult = await _acceptedIdentityBridgeRepository.SyncAcceptedIdentitiesAsync(
                run.IngestionRunId,
                selectedCohortName,
                _options.StartedBy,
                cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyPublicationCandidateGeneration",
                "AcceptedIdentityBridge",
                bridgeResult.Skipped
                    ? $"Accepted identity bridge skipped. Reason={bridgeResult.SkipReason}"
                    : $"AcceptedIdentityRows={bridgeResult.AcceptedIdentityRows}; AuthorCandidateRowsInserted={bridgeResult.AuthorCandidateRowsInserted}; AuthorCandidateRowsUpdated={bridgeResult.AuthorCandidateRowsUpdated}; AuthorDecisionRowsInserted={bridgeResult.AuthorDecisionRowsInserted}; ExistingAcceptedAuthorDecisionsSkipped={bridgeResult.ExistingAcceptedAuthorDecisionsSkipped}.",
                run.RunCorrelationId,
                cancellationToken);

            var rows = await _repository.GetPublicationCandidateSourceRowsAsync(
                _options.MaxPublicationCandidatesToGenerate,
                _options.OnlyCurrentAcceptedAuthorDecisions,
                selectedCohortName,
                cancellationToken);

            var candidateCountsByFaculty = selectedFacultyIds.ToDictionary(facultyId => facultyId, _ => 0);
            foreach (var group in rows.GroupBy(row => row.FacultyId))
            {
                candidateCountsByFaculty[group.Key] = group.Count();
            }

            var facultyWithZeroPublicationCandidates = candidateCountsByFaculty
                .Where(pair => pair.Value == 0)
                .Select(pair => pair.Key)
                .OrderBy(facultyId => facultyId)
                .ToList();

            var inserted = 0;
            var updated = 0;

            foreach (var row in rows)
            {
                var wasInserted = await _repository.UpsertPublicationCandidateAsync(
                    row,
                    run.IngestionRunId,
                    _options.PublicationCandidateStatus,
                    cancellationToken);

                if (wasInserted) inserted++; else updated++;
            }

            var evidenceEnhancementResult = await _publicationEvidenceEnhancementRepository.EnhancePublicationCandidatesAsync(
                run.IngestionRunId,
                selectedCohortName,
                cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyPublicationCandidateGeneration",
                "Patch007EvidenceEnhancement",
                evidenceEnhancementResult.Skipped
                    ? $"PATCH007 evidence enhancement skipped. Reason={evidenceEnhancementResult.SkipReason}"
                    : $"CandidateRowsEvaluated={evidenceEnhancementResult.CandidateRowsEvaluated}; CandidateRowsEnhanced={evidenceEnhancementResult.CandidateRowsEnhanced}; RowsWithAffiliatedInstitutionEvidence={evidenceEnhancementResult.RowsWithAffiliatedInstitutionEvidence}; RowsWithKnownCoauthorEvidence={evidenceEnhancementResult.RowsWithKnownCoauthorEvidence}.",
                run.RunCorrelationId,
                cancellationToken);

            var facultyProcessedCount = selectedCohortName is null
                ? rows.Select(row => row.FacultyId).Distinct().Count()
                : cohortFaculties.Count;

            var summary =
                $"Faculty-publication candidate generation completed. SelectedCohortName={selectedCohortName ?? "(none)"}; " +
                $"EnabledCohortFacultyCount={(selectedCohortName is null ? 0 : cohortFaculties.Count)}; FacultyIdsSelected={FormatIdList(selectedFacultyIds)}; " +
                $"AcceptedIdentityBridgeSkipped={bridgeResult.Skipped}; AcceptedIdentityRows={bridgeResult.AcceptedIdentityRows}; " +
                $"BridgeAuthorCandidatesInserted={bridgeResult.AuthorCandidateRowsInserted}; BridgeAuthorCandidatesUpdated={bridgeResult.AuthorCandidateRowsUpdated}; BridgeAuthorDecisionsInserted={bridgeResult.AuthorDecisionRowsInserted}; ExistingAcceptedAuthorDecisionsSkipped={bridgeResult.ExistingAcceptedAuthorDecisionsSkipped}; " +
                $"Patch007EvidenceSkipped={evidenceEnhancementResult.Skipped}; Patch007CandidateRowsEvaluated={evidenceEnhancementResult.CandidateRowsEvaluated}; Patch007CandidateRowsEnhanced={evidenceEnhancementResult.CandidateRowsEnhanced}; Patch007RowsWithAffiliatedInstitutionEvidence={evidenceEnhancementResult.RowsWithAffiliatedInstitutionEvidence}; Patch007RowsWithKnownCoauthorEvidence={evidenceEnhancementResult.RowsWithKnownCoauthorEvidence}; " +
                $"FacultyProcessed={facultyProcessedCount}; SourceRowsProcessed={rows.Count}; " +
                $"FacultyWithZeroPublicationCandidates={FormatIdList(facultyWithZeroPublicationCandidates)}; " +
                $"CandidateCountsByFaculty={FormatCandidateCounts(candidateCountsByFaculty)}; " +
                $"CandidatesInserted={inserted}; CandidatesUpdated={updated}.";

            await _repository.CompleteRunAsync(
                run.IngestionRunId,
                summary,
                authorsProcessed: bridgeResult.AcceptedIdentityRows,
                worksProcessed: rows.Count,
                cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyPublicationCandidateGeneration",
                "RunCompleted",
                summary,
                run.RunCorrelationId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _repository.FailRunAsync(run.IngestionRunId, ex, CancellationToken.None);

            await _logger.LogErrorAsync(
                "FacultyPublicationCandidateGeneration",
                "RunFailed",
                $"Faculty-publication candidate generation run {run.IngestionRunId} failed.",
                ex,
                run.RunCorrelationId,
                CancellationToken.None);

            throw;
        }
    }

    private static string? NormalizeCohortName(string? cohortName)
    {
        return string.IsNullOrWhiteSpace(cohortName) ? null : cohortName.Trim();
    }

    private static string FormatIdList(IEnumerable<long> values)
    {
        var ordered = values.OrderBy(value => value).ToList();
        return ordered.Count == 0 ? "(none)" : string.Join(",", ordered);
    }

    private static string FormatCandidateCounts(IReadOnlyDictionary<long, int> countsByFaculty)
    {
        if (countsByFaculty.Count == 0)
        {
            return "(none)";
        }

        return string.Join(",", countsByFaculty
            .OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}:{pair.Value}"));
    }
}
