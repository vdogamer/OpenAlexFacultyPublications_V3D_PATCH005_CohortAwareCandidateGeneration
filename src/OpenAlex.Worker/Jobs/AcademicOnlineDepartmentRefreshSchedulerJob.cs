using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class AcademicOnlineDepartmentRefreshSchedulerJob
{
    private readonly AcademicOnlineDepartmentRefreshRepository _refreshRepository;
    private readonly AcademicOnlineDepartmentSyncJob _syncJob;
    private readonly IOperationalLogger _logger;
    private readonly AcademicOnlineDepartmentRefreshSchedulerOptions _options;

    public AcademicOnlineDepartmentRefreshSchedulerJob(
        AcademicOnlineDepartmentRefreshRepository refreshRepository,
        AcademicOnlineDepartmentSyncJob syncJob,
        IOperationalLogger logger,
        IOptions<AcademicOnlineDepartmentRefreshSchedulerOptions> options)
    {
        _refreshRepository = refreshRepository;
        _syncJob = syncJob;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var leaseOwner =
            $"{Environment.MachineName}:{Environment.ProcessId}";

        var claims =
            await _refreshRepository.ClaimDueDepartmentsAsync(
                Math.Clamp(
                    _options.MaxDepartmentsPerPass,
                    1,
                    50),
                Math.Clamp(
                    _options.LeaseMinutes,
                    5,
                    1440),
                leaseOwner,
                cancellationToken);

        if (claims.Count == 0)
        {
            await _logger.LogInfoAsync(
                "AcademicOnlineDepartmentRefreshScheduler",
                "NoDepartmentsDue",
                "No enabled Academic Online departments are currently due for refresh.",
                cancellationToken: cancellationToken);
            return;
        }

        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AcademicOnlineDepartmentSyncOutcome outcome;

            try
            {
                outcome = await _syncJob.ExecuteDepartmentAsync(
                    new AcademicOnlineDepartmentSyncRequest
                    {
                        DepartmentName = claim.DepartmentName,
                        FromYear = claim.RefreshFromYear,
                        ToYear = claim.RefreshToYear,
                        VerifiedOnly = claim.RefreshVerifiedOnly,
                        MaxScholarsToProcess = claim.RefreshMaxScholars,
                        StartedBy = _options.StartedBy,
                        ApplicationVersion = _options.ApplicationVersion
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                outcome = AcademicOnlineDepartmentSyncOutcome.Failed(
                    runId: null,
                    ex.Message);
            }

            await _refreshRepository.CompleteClaimAsync(
                claim.DepartmentId,
                outcome.RunId,
                outcome.Succeeded,
                outcome.IsCacheComplete,
                claim.LeaseOwner,
                outcome.ErrorMessage,
                CancellationToken.None);

            var status =
                !outcome.Succeeded
                    ? "Failed"
                    : outcome.IsCacheComplete
                        ? "Completed"
                        : "Partial";

            await _logger.LogInfoAsync(
                "AcademicOnlineDepartmentRefreshScheduler",
                "DepartmentRefreshFinished",
                $"Department={claim.DepartmentName}; " +
                $"Status={status}; " +
                $"RunId={outcome.RunId?.ToString() ?? "(none)"}; " +
                $"Scholars={outcome.ScholarsProcessed}/{outcome.ScholarsDiscovered}; " +
                $"PublicationsProcessed={outcome.PublicationsProcessed}.",
                cancellationToken: CancellationToken.None);
        }
    }
}
