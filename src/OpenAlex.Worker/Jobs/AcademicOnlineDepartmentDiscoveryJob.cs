using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class AcademicOnlineDepartmentDiscoveryJob
{
    private readonly AcademicOnlineApiClient _client;
    private readonly AcademicOnlineDepartmentDiscoveryRepository _repository;
    private readonly IOperationalLogger _logger;

    public AcademicOnlineDepartmentDiscoveryJob(
        AcademicOnlineApiClient client,
        AcademicOnlineDepartmentDiscoveryRepository repository,
        IOperationalLogger logger)
    {
        _client = client;
        _repository = repository;
        _logger = logger;
    }

    public async Task ExecuteClaimAsync(
        AcademicOnlineDepartmentDiscoveryClaim claim,
        CancellationToken cancellationToken)
    {
        long? runId = null;
        AcademicOnlineDepartmentDiscoveryCollection? discovery = null;

        try
        {
            runId = await _repository.StartRunAsync(
                claim,
                startedBy:
                    "OpenAlex.V3E.PATCH021.Scheduler",
                applicationVersion:
                    "3E-PATCH021-DepartmentDiscoveryCatalogOnboarding",
                cancellationToken:
                    cancellationToken);

            var seeds =
                await _repository.GetActiveSeedsAsync(
                    claim.ParentName,
                    cancellationToken);

            if (seeds.Count > 0)
            {
                discovery =
                    await _client
                        .DiscoverDepartmentsFromSeedsAsync(
                            claim.ParentName,
                            seeds
                                .Select(seed =>
                                    new AcademicOnlineDepartmentSeedCandidate
                                    {
                                        OfficialName =
                                            seed.OfficialName,
                                        SearchName =
                                            seed.AcademicOnlineSearchName,
                                        SearchPriority =
                                            seed.SearchPriority
                                    })
                                .ToList(),
                            cancellationToken);
            }
            else
            {
                discovery =
                    await _client.DiscoverDepartmentsAsync(
                        claim.ParentName,
                        claim.RowsPerPage,
                        claim.MaxPages,
                        cancellationToken);
            }

            if (discovery.ScholarsScanned == 0)
            {
                throw new InvalidOperationException(
                    "Academic Online department discovery returned no scholars. " +
                    "The unfiltered scholar-search endpoint may not support this discovery strategy.");
            }

            if (discovery.Departments.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Academic Online department discovery found no direct child organizational units under '{claim.ParentName}'.");
            }

            var inserted = 0;
            var updated = 0;

            foreach (var department in discovery.Departments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var wasInserted =
                    await _repository.UpsertDiscoveredDepartmentAsync(
                        runId.Value,
                        new AcademicOnlineDiscoveredDepartmentRecord
                        {
                            AcademicOnlineDepartmentId =
                                department.AcademicOnlineDepartmentId,
                            DepartmentName =
                                department.DepartmentName,
                            Slug =
                                department.Slug,
                            ParentAcademicOnlineDepartmentId =
                                department.ParentAcademicOnlineDepartmentId,
                            ParentName =
                                department.ParentName,
                            ParentSlug =
                                department.ParentSlug,
                            ScholarCount =
                                department.ScholarCount
                        },
                        cancellationToken);

                if (wasInserted)
                {
                    inserted++;
                }
                else
                {
                    updated++;
                }
            }

            var status =
                discovery.HitMaxPages ||
                discovery.ProfileValidationFailures > 0 ||
                discovery.DepartmentsUnresolved > 0
                    ? "Partial"
                    : "Completed";

            var summary =
                $"Academic Online department discovery {status.ToLowerInvariant()}. " +
                $"Parent={claim.ParentName}; " +
                $"PagesFetched={discovery.PagesFetched}; " +
                $"ScholarsScanned={discovery.ScholarsScanned}; " +
                $"AffiliationsObserved={discovery.AffiliationsObserved}; " +
                $"ProfilesFetched={discovery.ProfilesFetched}; " +
                $"ProfileValidationFailures={discovery.ProfileValidationFailures}; " +
                $"DepartmentsUnresolved={discovery.DepartmentsUnresolved}; " +
                $"DepartmentsDiscovered={discovery.Departments.Count}; " +
                $"DepartmentsInserted={inserted}; " +
                $"DepartmentsUpdated={updated}; " +
                $"HitMaxPages={discovery.HitMaxPages}.";

            await _repository.CompleteAsync(
                claim,
                runId.Value,
                status,
                discovery.PagesFetched,
                discovery.ScholarsScanned,
                discovery.AffiliationsObserved,
                discovery.Departments.Count,
                inserted,
                updated,
                discovery.HitMaxPages,
                summary,
                cancellationToken);

            await _logger.LogInfoAsync(
                "AcademicOnlineDepartmentDiscovery",
                "DiscoveryFinished",
                summary,
                cancellationToken:
                    CancellationToken.None);
        }
        catch (Exception ex)
        {
            try
            {
                await _repository.FailAsync(
                    claim,
                    runId,
                    ex,
                    pagesFetched:
                        discovery?.PagesFetched ?? 0,
                    scholarsScanned:
                        discovery?.ScholarsScanned ?? 0,
                    affiliationsObserved:
                        discovery?.AffiliationsObserved ?? 0,
                    hitMaxPages:
                        discovery?.HitMaxPages ?? false,
                    cancellationToken:
                        CancellationToken.None);
            }
            catch
            {
                // Preserve the original discovery failure for operational logging.
            }

            await _logger.LogErrorAsync(
                "AcademicOnlineDepartmentDiscovery",
                "DiscoveryFailed",
                $"Academic Online department discovery request {claim.RequestId} failed.",
                ex,
                cancellationToken:
                    CancellationToken.None);
        }
    }
}
