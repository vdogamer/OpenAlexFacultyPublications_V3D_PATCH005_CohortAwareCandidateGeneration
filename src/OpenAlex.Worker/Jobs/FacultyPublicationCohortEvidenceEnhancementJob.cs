using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class FacultyPublicationCohortEvidenceEnhancementJob
{
    private readonly PublicationEvidenceEnhancementRepository _enhancementRepository;
    private readonly FacultyPublicationCohortEvidenceEnhancementAuditRepository _auditRepository;
    private readonly IOperationalLogger _logger;
    private readonly FacultyPublicationCohortEvidenceEnhancementOptions _options;

    public FacultyPublicationCohortEvidenceEnhancementJob(
        PublicationEvidenceEnhancementRepository enhancementRepository,
        FacultyPublicationCohortEvidenceEnhancementAuditRepository auditRepository,
        IOperationalLogger logger,
        IOptions<FacultyPublicationCohortEvidenceEnhancementOptions> options)
    {
        _enhancementRepository = enhancementRepository;
        _auditRepository = auditRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!_options.ConfirmEnhancement)
        {
            throw new InvalidOperationException(
                "FacultyPublicationCohortEvidenceEnhancement:ConfirmEnhancement must be true.");
        }

        var cohortName = string.IsNullOrWhiteSpace(_options.CohortName)
            ? throw new InvalidOperationException(
                "FacultyPublicationCohortEvidenceEnhancement:CohortName is required.")
            : _options.CohortName.Trim();

        var sourceCandidateIngestionRunId = _options.SourceCandidateIngestionRunId;
        if (sourceCandidateIngestionRunId <= 0)
        {
            throw new InvalidOperationException(
                "FacultyPublicationCohortEvidenceEnhancement:SourceCandidateIngestionRunId must be > 0.");
        }

        var expectedCandidateCount = Math.Max(1, _options.ExpectedCandidateCount);
        var startedBy = FitRequired(_options.StartedBy, 256, "StartedBy");
        var applicationVersion = FitRequired(_options.ApplicationVersion, 50, "ApplicationVersion");

        var before = await _auditRepository.GetSafetyCountsAsync(
            sourceCandidateIngestionRunId,
            cohortName,
            cancellationToken);

        if (before.SourceCandidateRows != expectedCandidateCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCandidateCount} source candidates, found {before.SourceCandidateRows}. Aborting.");
        }

        if (before.SourceCandidatesWithPatch007Evidence > 0)
        {
            throw new InvalidOperationException(
                $"PATCH007 evidence already exists on {before.SourceCandidatesWithPatch007Evidence} source candidates. Aborting to avoid double-boosting.");
        }

        var auditRunId = await _auditRepository.StartRunAsync(
            cohortName,
            sourceCandidateIngestionRunId,
            expectedCandidateCount,
            before,
            startedBy,
            applicationVersion,
            cancellationToken);

        await _logger.LogInfoAsync(
            "FacultyPublicationCohortEvidenceEnhancement",
            "RunStarted",
            $"Started PATCH017B11A evidence enhancement run {auditRunId}. CohortName={cohortName}; SourceCandidateIngestionRunId={sourceCandidateIngestionRunId}; ExpectedCandidateCount={expectedCandidateCount}.",
            cancellationToken: cancellationToken);

        try
        {
            var result = await _enhancementRepository.EnhancePublicationCandidatesAsync(
                sourceCandidateIngestionRunId,
                cohortName,
                cancellationToken);

            if (result.Skipped)
            {
                throw new InvalidOperationException(
                    $"Publication evidence enhancement was skipped: {result.SkipReason}");
            }

            var after = await _auditRepository.GetSafetyCountsAsync(
                sourceCandidateIngestionRunId,
                cohortName,
                cancellationToken);

            if (after.CandidateRowsTotal != before.CandidateRowsTotal)
            {
                throw new InvalidOperationException(
                    "Total publication candidate row count changed during evidence enhancement.");
            }

            if (after.PublicationDecisionRows != before.PublicationDecisionRows)
            {
                throw new InvalidOperationException(
                    "Publication decision row count changed during evidence enhancement.");
            }

            if (after.SourceCandidateRows != expectedCandidateCount)
            {
                throw new InvalidOperationException(
                    "Source candidate count changed during evidence enhancement.");
            }

            if (after.SourceCandidatesWithPatch007Evidence != expectedCandidateCount)
            {
                throw new InvalidOperationException(
                    $"Expected PATCH007 evidence on {expectedCandidateCount} rows, found {after.SourceCandidatesWithPatch007Evidence}.");
            }

            var summary =
                $"PATCH017B11A evidence enhancement completed. CohortName={cohortName}; SourceCandidateIngestionRunId={sourceCandidateIngestionRunId}; " +
                $"CandidateRowsEvaluated={result.CandidateRowsEvaluated}; CandidateRowsEnhanced={result.CandidateRowsEnhanced}; " +
                $"RowsWithAffiliatedInstitutionEvidence={result.RowsWithAffiliatedInstitutionEvidence}; RowsWithKnownCoauthorEvidence={result.RowsWithKnownCoauthorEvidence}; " +
                $"CandidateRowsTotalBefore={before.CandidateRowsTotal}; CandidateRowsTotalAfter={after.CandidateRowsTotal}; " +
                $"PublicationDecisionRowsBefore={before.PublicationDecisionRows}; PublicationDecisionRowsAfter={after.PublicationDecisionRows}; " +
                $"SourceCandidatesWithEvidenceAfter={after.SourceCandidatesWithPatch007Evidence}.";

            await _auditRepository.CompleteRunAsync(
                auditRunId,
                after,
                result.CandidateRowsEvaluated,
                result.CandidateRowsEnhanced,
                result.RowsWithAffiliatedInstitutionEvidence,
                result.RowsWithKnownCoauthorEvidence,
                summary,
                cancellationToken);

            await _logger.LogInfoAsync(
                "FacultyPublicationCohortEvidenceEnhancement",
                "RunCompleted",
                summary,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await _auditRepository.FailRunAsync(
                auditRunId,
                ex,
                CancellationToken.None);

            await _logger.LogErrorAsync(
                "FacultyPublicationCohortEvidenceEnhancement",
                "RunFailed",
                $"PATCH017B11A evidence enhancement run {auditRunId} failed.",
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
                $"FacultyPublicationCohortEvidenceEnhancement:{optionName} is required.");
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
