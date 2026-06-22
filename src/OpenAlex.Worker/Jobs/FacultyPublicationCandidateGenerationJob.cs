using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class FacultyPublicationCandidateGenerationJob
{
    private readonly CandidateGenerationRepository _repository;
    private readonly IOperationalLogger _logger;
    private readonly CandidateGenerationOptions _options;

    public FacultyPublicationCandidateGenerationJob(
        CandidateGenerationRepository repository,
        IOperationalLogger logger,
        IOptions<CandidateGenerationOptions> options)
    {
        _repository = repository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var run = await _repository.StartRunAsync(
            _options.RunMode,
            (string.IsNullOrWhiteSpace(_options.PilotCohortName)
                ? "FacultyPublicationCandidateGeneration"
                : $"FacultyPublicationCandidateGeneration; PilotCohortName={_options.PilotCohortName}"),
            null,
            _options.MaxPublicationCandidatesToGenerate,
            _options.ApplicationVersion,
            _options.StartedBy,
            cancellationToken);

        await _logger.LogInfoAsync(
            "FacultyPublicationCandidateGeneration",
            "RunStarted",
            $"Started faculty-publication candidate generation run {run.IngestionRunId}. PilotCohortName={_options.PilotCohortName ?? "(none)"}.",
            run.RunCorrelationId,
            cancellationToken);

        try
        {
            var cohortSnapshot = await _repository.GetPilotCohortFacultySnapshotAsync(_options.PilotCohortName, cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyPublicationCandidateGeneration",
                "CohortSelection",
                $"SelectedCohortName={FormatCohortName(_options.PilotCohortName)}; " +
                $"EnabledCohortFacultyCount={FormatEnabledCohortFacultyCount(cohortSnapshot)}; " +
                $"EnabledCohortFacultyIds={FormatIds(cohortSnapshot?.EnabledActiveFacultyIds)}.",
                run.RunCorrelationId,
                cancellationToken);

            var rows = await _repository.GetPublicationCandidateSourceRowsAsync(
                _options.MaxPublicationCandidatesToGenerate,
                _options.OnlyCurrentAcceptedAuthorDecisions,
                _options.PilotCohortName,
                cancellationToken);

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

            var facultyProcessedIds = rows
                .Select(row => row.FacultyId)
                .Distinct()
                .OrderBy(facultyId => facultyId)
                .ToList();
            var publicationCandidateCountsByFaculty = rows
                .GroupBy(row => row.FacultyId)
                .ToDictionary(group => group.Key, group => group.Count());

            var summary =
                "Faculty-publication candidate generation completed. " +
                $"SelectedCohortName={FormatCohortName(_options.PilotCohortName)}; " +
                $"EnabledCohortFacultyCount={FormatEnabledCohortFacultyCount(cohortSnapshot)}; " +
                $"SelectedFacultyIds={FormatIds(cohortSnapshot?.EnabledActiveFacultyIds)}; " +
                $"FacultyProcessed={facultyProcessedIds.Count}; FacultyProcessedIds={FormatIds(facultyProcessedIds)}; " +
                $"PublicationCandidateCountsByFaculty={FormatCandidateCounts(publicationCandidateCountsByFaculty)}; " +
                $"SourceRowsProcessed={rows.Count}; CandidatesInserted={inserted}; CandidatesUpdated={updated}.";

            await _repository.CompleteRunAsync(
                run.IngestionRunId,
                summary,
                authorsProcessed: 0,
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

    private static string FormatCohortName(string? cohortName)
    {
        return string.IsNullOrWhiteSpace(cohortName) ? "(none)" : cohortName.Trim();
    }

    private static string FormatEnabledCohortFacultyCount(PilotCohortFacultySnapshot? snapshot)
    {
        return snapshot is null ? "(not configured)" : snapshot.EnabledActiveFacultyCount.ToString();
    }

    private static string FormatIds(IEnumerable<long>? facultyIds)
    {
        if (facultyIds is null)
        {
            return "[]";
        }

        var ids = facultyIds.ToList();
        return ids.Count == 0 ? "[]" : "[" + string.Join(",", ids) + "]";
    }

    private static string FormatCandidateCounts(IReadOnlyDictionary<long, int> candidateCountsByFaculty)
    {
        if (candidateCountsByFaculty.Count == 0)
        {
            return "[]";
        }

        return "[" + string.Join("; ", candidateCountsByFaculty
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Key + "=" + pair.Value)) + "]";
    }
}
