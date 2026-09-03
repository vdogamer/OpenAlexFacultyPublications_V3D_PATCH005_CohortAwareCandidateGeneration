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
    private readonly WorkerOptions _workerOptions;

    public AcademicOnlineDepartmentRefreshSchedulerJob(
        AcademicOnlineDepartmentRefreshRepository refreshRepository,
        AcademicOnlineDepartmentSyncJob syncJob,
        IOperationalLogger logger,
        IOptions<AcademicOnlineDepartmentRefreshSchedulerOptions> options,
        IOptions<WorkerOptions> workerOptions)
    {
        _refreshRepository = refreshRepository;
        _syncJob = syncJob;
        _logger = logger;
        _options = options.Value;
        _workerOptions = workerOptions.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var schedulerKey =
            string.IsNullOrWhiteSpace(_options.SchedulerKey)
                ? "AcademicOnlineDepartmentRefreshScheduler"
                : _options.SchedulerKey.Trim();

        var leaseOwner =
            $"{Environment.MachineName}:{Environment.ProcessId}";

        var pollIntervalSeconds =
            Math.Max(10, _workerOptions.DelaySeconds);

        var isContinuous =
            !_workerOptions.RunOnceAndStop;

        var departmentsClaimed = 0;
        var departmentsCompleted = 0;
        var departmentsPartial = 0;
        var departmentsFailed = 0;

        await _refreshRepository.RecordSchedulerPassStartedAsync(
            schedulerKey,
            leaseOwner,
            pollIntervalSeconds,
            isContinuous,
            cancellationToken);

        try
        {
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

            departmentsClaimed = claims.Count;

            if (claims.Count == 0)
            {
                await _refreshRepository.RecordSchedulerPassCompletedAsync(
                    schedulerKey,
                    departmentsClaimed: 0,
                    departmentsCompleted: 0,
                    departmentsPartial: 0,
                    departmentsFailed: 0,
                    cancellationToken);

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

                if (!outcome.Succeeded)
                {
                    departmentsFailed++;
                }
                else if (outcome.IsCacheComplete)
                {
                    departmentsCompleted++;
                }
                else
                {
                    departmentsPartial++;
                }

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

            await _refreshRepository.RecordSchedulerPassCompletedAsync(
                schedulerKey,
                departmentsClaimed,
                departmentsCompleted,
                departmentsPartial,
                departmentsFailed,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            try
            {
                await _refreshRepository.RecordSchedulerPassFailedAsync(
                    schedulerKey,
                    departmentsClaimed,
                    departmentsCompleted,
                    departmentsPartial,
                    departmentsFailed,
                    ex,
                    CancellationToken.None);
            }
            catch
            {
                // Preserve the original scheduler failure for the outer worker logger.
            }

            throw;
        }
    }
}
