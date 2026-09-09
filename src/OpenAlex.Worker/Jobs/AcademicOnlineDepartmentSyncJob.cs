using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;

namespace OpenAlex.Worker.Jobs;

public sealed class AcademicOnlineDepartmentSyncJob
{
    private readonly AcademicOnlineApiClient _client;
    private readonly OpenAlexApiClient _openAlexClient;
    private readonly AcademicOnlineReportingStoreRepository _repository;
    private readonly IOperationalLogger _logger;
    private readonly AcademicOnlineDepartmentSyncOptions _options;
    private readonly OpenAlexOptions _openAlexOptions;

    public AcademicOnlineDepartmentSyncJob(
        AcademicOnlineApiClient client,
        OpenAlexApiClient openAlexClient,
        AcademicOnlineReportingStoreRepository repository,
        IOperationalLogger logger,
        IOptions<AcademicOnlineDepartmentSyncOptions> options,
        IOptions<OpenAlexOptions> openAlexOptions)
    {
        _client = client;
        _openAlexClient = openAlexClient;
        _repository = repository;
        _logger = logger;
        _options = options.Value;
        _openAlexOptions = openAlexOptions.Value;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var outcome = await ExecuteDepartmentAsync(
            new AcademicOnlineDepartmentSyncRequest
            {
                DepartmentName = _options.DepartmentName,
                FromYear = _options.FromYear,
                ToYear = _options.ToYear,
                VerifiedOnly = _options.VerifiedOnly,
                MaxScholarsToProcess = _options.MaxScholarsToProcess,
                StartedBy = _options.StartedBy,
                ApplicationVersion = _options.ApplicationVersion
            },
            cancellationToken);

        if (!outcome.Succeeded)
        {
            throw new InvalidOperationException(
                outcome.ErrorMessage ??
                "Academic Online department sync failed.");
        }
    }

    public async Task<AcademicOnlineDepartmentSyncOutcome>
        ExecuteDepartmentAsync(
            AcademicOnlineDepartmentSyncRequest request,
            CancellationToken cancellationToken)
    {
        var departmentName = request.DepartmentName?.Trim();

        if (string.IsNullOrWhiteSpace(departmentName))
        {
            return AcademicOnlineDepartmentSyncOutcome.Failed(
                runId: null,
                "Academic Online department name is required.");
        }

        try
        {
            ValidateYearRange(
                request.FromYear,
                request.ToYear);
        }
        catch (Exception ex)
        {
            return AcademicOnlineDepartmentSyncOutcome.Failed(
                runId: null,
                ex.Message);
        }

        var maxScholars = Math.Clamp(
            request.MaxScholarsToProcess,
            1,
            5000);

        var startedBy =
            string.IsNullOrWhiteSpace(request.StartedBy)
                ? "OpenAlex.V3E.PATCH020"
                : request.StartedBy.Trim();

        var applicationVersion =
            string.IsNullOrWhiteSpace(request.ApplicationVersion)
                ? "3E-PATCH020-DepartmentCatalogScheduledRefresh"
                : request.ApplicationVersion.Trim();

        long runId;

        try
        {
            runId = await _repository.StartRunAsync(
                departmentName,
                request.FromYear,
                request.ToYear,
                request.VerifiedOnly,
                maxScholars,
                startedBy,
                applicationVersion,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return AcademicOnlineDepartmentSyncOutcome.Failed(
                runId: null,
                ex.Message);
        }

        var scholarsProcessed = 0;
        var publicationRowsProcessed = 0;
        var scholarPagesFetched = 0;
        var publicationPagesFetched = 0;
        var publicationPaginationSafeguardHitCount = 0;

        try
        {
            var selection =
                await ResolveScholarSelectionAsync(
                    request,
                    departmentName,
                    cancellationToken);

            scholarPagesFetched =
                selection.ScholarPagesFetched;

            if (selection.Scholars.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Academic Online returned no usable scholars for department '{departmentName}' " +
                    $"using source mode '{selection.SourceMode}'.");
            }

            var departmentId =
                await _repository.UpsertDepartmentAsync(
                    runId,
                    departmentName,
                    cancellationToken);

            if (request.DepartmentId > 0 &&
                departmentId != request.DepartmentId)
            {
                throw new InvalidOperationException(
                    $"Scheduled department ID {request.DepartmentId} did not match the reporting-store department ID {departmentId} for '{departmentName}'.");
            }

            var scholars =
                selection.Scholars
                    .Take(maxScholars)
                    .ToList();

            var rosterAcademicOnlineSources =
                selection.RosterPublicationSources
                    .Where(item =>
                        string.Equals(
                            item.SourceProvider,
                            "AcademicOnline",
                            StringComparison.OrdinalIgnoreCase))
                    .GroupBy(
                        item => item.SourceAuthorId,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group =>
                            (IReadOnlyList<RosterPublicationSource>)
                            group.ToList(),
                        StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in scholars)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var generalDocument =
                    await _client.GetAuthorGeneralAsync(
                        candidate.AuthorId,
                        cancellationToken);

                var general =
                    generalDocument.RootElement;

                var displayName =
                    GetString(
                        general,
                        "name")
                    ?? candidate.Name
                    ?? candidate.AuthorId;

                var email =
                    GetString(
                        general,
                        "email")
                    ?? candidate.Email;

                var position =
                    GetString(
                        general,
                        "position")
                    ?? candidate.Position;

                var institution =
                    GetString(
                        general,
                        "institution")
                    ?? candidate.Institution;

                var orcidUrl =
                    GetString(
                        general,
                        "orcid_url")
                    ?? candidate.OrcidUrl;

                await _repository.UpsertScholarAsync(
                    runId,
                    candidate.AuthorId,
                    displayName,
                    email,
                    position,
                    institution,
                    orcidUrl,
                    GetInt(
                        general,
                        "publication_count"),
                    GetBool(
                        general,
                        "has_publications"),
                    GetBool(
                        general,
                        "has_verified_publications"),
                    general.GetRawText(),
                    cancellationToken);

                await _repository
                    .TouchScholarDepartmentAsync(
                        runId,
                        candidate.AuthorId,
                        departmentId,
                        cancellationToken);

                var publications =
                    await _client
                        .GetAllAuthorPublicationsAsync(
                            candidate.AuthorId,
                            request.VerifiedOnly,
                            rowsPerPage:
                                Math.Clamp(
                                    _options.PublicationRowsPerPage,
                                    1,
                                    100),
                            maxPages:
                                Math.Clamp(
                                    _options.MaxPublicationPages,
                                    1,
                                    1000),
                            fromYear:
                                request.FromYear,
                            toYear:
                                request.ToYear,
                            cancellationToken:
                                cancellationToken);

                publicationPagesFetched +=
                    publications.PagesFetched;

                if (publications.HitMaxPages)
                {
                    publicationPaginationSafeguardHitCount++;
                }

                rosterAcademicOnlineSources.TryGetValue(
                    candidate.AuthorId,
                    out var matchingRosterSources);

                foreach (var publication in
                         publications.Publications)
                {
                    var publicationKey =
                        await _repository.UpsertPublicationAsync(
                            runId,
                            publication.Id,
                            publication.OpenAlexId,
                            publication.Doi,
                            publication.Title,
                            publication.Venue,
                            publication.YearText,
                            publication.PublicationYear,
                            publication.CitationCount,
                            publication.Verified,
                            publication.Link,
                            publication.CoauthorsJson,
                            publication.KeywordsJson,
                            publication.RawJson,
                            cancellationToken);

                    await _repository
                        .TouchScholarPublicationAsync(
                            runId,
                            candidate.AuthorId,
                            publicationKey,
                            cancellationToken);

                    await _repository
                        .TouchDepartmentPublicationAsync(
                            runId,
                            departmentId,
                            publicationKey,
                            cancellationToken);

                    await _repository
                        .TouchDepartmentScholarPublicationAsync(
                            runId,
                            departmentId,
                            candidate.AuthorId,
                            publicationKey,
                            cancellationToken);

                    if (matchingRosterSources is not null)
                    {
                        foreach (var rosterSource in
                                 matchingRosterSources)
                        {
                            await _repository
                                .TouchDepartmentRosterPublicationAsync(
                                    runId,
                                    departmentId,
                                    rosterSource.RosterMemberId,
                                    publicationKey,
                                    "AcademicOnline",
                                    candidate.AuthorId,
                                    cancellationToken);
                        }
                    }

                    publicationRowsProcessed++;
                }

                scholarsProcessed++;
            }

            var openAlexRosterSources =
                selection.RosterPublicationSources
                    .Where(item =>
                        string.Equals(
                            item.SourceProvider,
                            "OpenAlex",
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item =>
                        item.DisplayName,
                        StringComparer.OrdinalIgnoreCase)
                    .Take(
                        Math.Max(
                            0,
                            maxScholars - scholarsProcessed))
                    .ToList();

            foreach (var rosterSource in
                     openAlexRosterSources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fallbackResult =
                    await ProcessOpenAlexRosterFallbackAsync(
                        runId,
                        departmentId,
                        rosterSource,
                        request,
                        cancellationToken);

                publicationRowsProcessed +=
                    fallbackResult.PublicationsProcessed;

                publicationPagesFetched +=
                    fallbackResult.PagesFetched;

                if (fallbackResult.HitMaxPages)
                {
                    publicationPaginationSafeguardHitCount++;
                }

                scholarsProcessed++;
            }

            var scholarsDiscovered =
                selection.ScholarsDiscovered;

            var isCacheComplete =
                selection.SourceMembershipComplete &&
                !selection.ScholarPaginationSafeguardHit &&
                scholarsProcessed == scholarsDiscovered &&
                publicationPaginationSafeguardHitCount == 0;

            var summary =
                $"Academic Online department sync completed. " +
                $"Department={departmentName}; " +
                $"SourceMode={selection.SourceMode}; " +
                $"SourceMembershipComplete={selection.SourceMembershipComplete}; " +
                $"SourceDetail={selection.SourceDetail}; " +
                $"ScholarsDiscovered={scholarsDiscovered}; " +
                $"ScholarsProcessed={scholarsProcessed}; " +
                $"MaxScholarsToProcess={maxScholars}; " +
                $"PublicationRowsProcessed={publicationRowsProcessed}; " +
                $"ScholarPagesFetched={scholarPagesFetched}; " +
                $"PublicationPagesFetched={publicationPagesFetched}; " +
                $"FromYear={request.FromYear?.ToString() ?? "(all)"}; " +
                $"ToYear={request.ToYear?.ToString() ?? "(current)"}; " +
                $"VerifiedOnly={request.VerifiedOnly}; " +
                $"ScholarPaginationSafeguardHit={selection.ScholarPaginationSafeguardHit}; " +
                $"PublicationPaginationSafeguardHitCount={publicationPaginationSafeguardHitCount}; " +
                $"CacheComplete={isCacheComplete}.";

            await _repository.CompleteRunAsync(
                runId,
                scholarsDiscovered,
                scholarsProcessed,
                publicationRowsProcessed,
                scholarPagesFetched,
                publicationPagesFetched,
                selection.ScholarPaginationSafeguardHit,
                publicationPaginationSafeguardHitCount,
                isCacheComplete,
                summary,
                cancellationToken);

            if (isCacheComplete)
            {
                await _repository.PromoteDepartmentSnapshotAsync(
                    runId,
                    departmentId,
                    cancellationToken);
            }

            await _logger.LogInfoAsync(
                "AcademicOnlineDepartmentSync",
                "RunCompleted",
                summary,
                cancellationToken: cancellationToken);

            return new AcademicOnlineDepartmentSyncOutcome
            {
                RunId = runId,
                Succeeded = true,
                IsCacheComplete = isCacheComplete,
                ScholarsDiscovered = scholarsDiscovered,
                ScholarsProcessed = scholarsProcessed,
                PublicationsProcessed = publicationRowsProcessed
            };
        }
        catch (Exception ex)
        {
            await _repository.FailRunAsync(
                runId,
                ex,
                CancellationToken.None);

            await _logger.LogErrorAsync(
                "AcademicOnlineDepartmentSync",
                "RunFailed",
                $"Academic Online department sync run {runId} failed.",
                ex,
                cancellationToken: CancellationToken.None);

            return AcademicOnlineDepartmentSyncOutcome.Failed(
                runId,
                ex.Message);
        }
    }

    private async Task<ScholarSelection> ResolveScholarSelectionAsync(
        AcademicOnlineDepartmentSyncRequest request,
        string departmentName,
        CancellationToken cancellationToken)
    {
        var sourceMode =
            string.IsNullOrWhiteSpace(request.SourceMode)
                ? "DirectDepartment"
                : request.SourceMode.Trim();

        return sourceMode switch
        {
            "AggregateUnits" =>
                await ResolveAggregateUnitScholarsAsync(
                    request.DepartmentId,
                    cancellationToken),

            "FacultyRoster" =>
                await ResolveFacultyRosterScholarsAsync(
                    request.DepartmentId,
                    request.VerifiedOnly,
                    cancellationToken),

            _ =>
                await ResolveDirectDepartmentScholarsAsync(
                    departmentName,
                    cancellationToken)
        };
    }

    private async Task<ScholarSelection> ResolveDirectDepartmentScholarsAsync(
        string departmentName,
        CancellationToken cancellationToken)
    {
        var departmentScholars =
            await _client.GetDepartmentScholarsAsync(
                departmentName,
                rowsPerPage: Math.Clamp(
                    _options.ScholarRowsPerPage,
                    1,
                    100),
                maxPages: Math.Clamp(
                    _options.MaxScholarPages,
                    1,
                    1000),
                cancellationToken: cancellationToken);

        return new ScholarSelection
        {
            SourceMode = "DirectDepartment",
            Scholars = departmentScholars.Scholars,
            ScholarsDiscovered = departmentScholars.Scholars.Count,
            ScholarPagesFetched = departmentScholars.PagesFetched,
            ScholarPaginationSafeguardHit = departmentScholars.HitMaxPages,
            SourceMembershipComplete = !departmentScholars.HitMaxPages,
            SourceDetail =
                $"Direct department query; RowsPerPage={departmentScholars.RowsPerPage}; " +
                $"PagesFetched={departmentScholars.PagesFetched}."
        };
    }

    private async Task<ScholarSelection> ResolveAggregateUnitScholarsAsync(
        long departmentId,
        CancellationToken cancellationToken)
    {
        if (departmentId <= 0)
        {
            throw new InvalidOperationException(
                "AggregateUnits source mode requires a persisted department ID.");
        }

        var configuredUnits =
            await _repository.GetDepartmentSourceUnitsAsync(
                departmentId,
                cancellationToken);

        if (configuredUnits.Count == 0)
        {
            throw new InvalidOperationException(
                "AggregateUnits source mode has no configured source units.");
        }

        var scholarsById =
            new Dictionary<string, AcademicOnlineScholarSearchCandidate>(
                StringComparer.OrdinalIgnoreCase);

        var pagesFetched = 0;
        var paginationSafeguardHit = false;
        var unresolvedUnits = new List<string>();
        var resolvedUnits = 0;

        foreach (var group in configuredUnits
            .GroupBy(
                item => item.OfficialUnitName,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                item => item.Key,
                StringComparer.OrdinalIgnoreCase))
        {
            var unitResolved = false;

            foreach (var alias in group.OrderBy(item => item.SearchPriority))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var result =
                        await _client.GetDepartmentScholarsAsync(
                            alias.AcademicOnlineSearchName,
                            rowsPerPage: Math.Clamp(
                                _options.ScholarRowsPerPage,
                                1,
                                100),
                            maxPages: Math.Clamp(
                                _options.MaxScholarPages,
                                1,
                                1000),
                            cancellationToken: cancellationToken);

                    pagesFetched += result.PagesFetched;
                    paginationSafeguardHit |= result.HitMaxPages;

                    if (result.Scholars.Count == 0)
                    {
                        await _repository.RecordSourceUnitAttemptAsync(
                            alias.SourceUnitId,
                            "NoScholars",
                            0,
                            errorMessage: null,
                            cancellationToken: cancellationToken);
                        continue;
                    }

                    var aliasValidated =
                        await ValidateAggregateAliasAsync(
                            alias,
                            result.Scholars,
                            cancellationToken);

                    if (!aliasValidated)
                    {
                        await _repository.RecordSourceUnitAttemptAsync(
                            alias.SourceUnitId,
                            "Rejected",
                            result.Scholars.Count,
                            "Returned scholars did not expose the configured division in sampled Academic Online profiles.",
                            cancellationToken: CancellationToken.None);
                        continue;
                    }

                    foreach (var scholar in result.Scholars)
                    {
                        scholarsById[scholar.AuthorId] = scholar;
                    }

                    await _repository.RecordSourceUnitAttemptAsync(
                        alias.SourceUnitId,
                        result.HitMaxPages ? "Partial" : "Resolved",
                        result.Scholars.Count,
                        errorMessage: null,
                        cancellationToken: cancellationToken);

                    unitResolved = true;
                    resolvedUnits++;
                    break;
                }
                catch (Exception ex)
                    when (ex is HttpRequestException ||
                          ex is TaskCanceledException)
                {
                    await _repository.RecordSourceUnitAttemptAsync(
                        alias.SourceUnitId,
                        "Failed",
                        0,
                        ex.Message,
                        CancellationToken.None);
                }
            }

            if (!unitResolved)
            {
                unresolvedUnits.Add(group.Key);
            }
        }

        return new ScholarSelection
        {
            SourceMode = "AggregateUnits",
            Scholars =
                scholarsById.Values
                    .OrderBy(
                        item => item.Name ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            ScholarsDiscovered = scholarsById.Count,
            ScholarPagesFetched = pagesFetched,
            ScholarPaginationSafeguardHit = paginationSafeguardHit,
            SourceMembershipComplete =
                unresolvedUnits.Count == 0 &&
                !paginationSafeguardHit,
            SourceDetail =
                $"Aggregate units resolved={resolvedUnits}/{configuredUnits.Select(item => item.OfficialUnitName).Distinct(StringComparer.OrdinalIgnoreCase).Count()}; " +
                $"Unresolved={string.Join(",", unresolvedUnits)}."
        };
    }

    private async Task<bool> ValidateAggregateAliasAsync(
        AcademicOnlineDepartmentSourceUnit alias,
        IReadOnlyList<AcademicOnlineScholarSearchCandidate> scholars,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in scholars.Take(5))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var profile =
                await _client.GetAuthorGeneralAsync(
                    candidate.AuthorId,
                    cancellationToken);

            var affiliations =
                AcademicOnlineDepartmentParser
                    .GetDepartmentAffiliations(
                        profile.RootElement);

            if (affiliations.Any(item =>
                    SourceUnitNamesEquivalent(
                        item.Name,
                        alias.AcademicOnlineSearchName)
                    ||
                    SourceUnitNamesEquivalent(
                        item.Name,
                        alias.OfficialUnitName)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SourceUnitNamesEquivalent(
        string? left,
        string? right)
    {
        return string.Equals(
            NormalizeSourceUnitName(left),
            NormalizeSourceUnitName(right),
            StringComparison.Ordinal);
    }

    private static string NormalizeSourceUnitName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized =
            value.Trim().ToLowerInvariant();

        foreach (var prefix in new[]
                 {
                     "division of ",
                     "department of "
                 })
        {
            if (normalized.StartsWith(
                    prefix,
                    StringComparison.Ordinal))
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        }

        normalized =
            normalized.Replace(
                "&",
                "and",
                StringComparison.Ordinal);

        return new string(
            normalized
                .Where(char.IsLetterOrDigit)
                .ToArray());
    }

    private async Task<ScholarSelection> ResolveFacultyRosterScholarsAsync(
        long departmentId,
        bool verifiedOnly,
        CancellationToken cancellationToken)
    {
        if (departmentId <= 0)
        {
            throw new InvalidOperationException(
                "FacultyRoster source mode requires a persisted department ID.");
        }

        var roster =
            await _repository.GetDepartmentRosterMembersAsync(
                departmentId,
                cancellationToken);

        if (roster.Count == 0)
        {
            throw new InvalidOperationException(
                "FacultyRoster source mode has no active roster members.");
        }

        var scholarsById =
            new Dictionary<string, AcademicOnlineScholarSearchCandidate>(
                StringComparer.OrdinalIgnoreCase);

        var rosterSources =
            new List<RosterPublicationSource>();

        var searchRequests = 0;
        var unresolvedMembers = new List<string>();

        foreach (var member in roster)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AcademicOnlineScholarSearchCandidate? resolved = null;
            string? resolutionMethod = null;
            string? resolutionDiagnostic = null;

            if (!string.IsNullOrWhiteSpace(
                    member.AcademicOnlineAuthorId))
            {
                resolved =
                    new AcademicOnlineScholarSearchCandidate
                    {
                        AuthorId =
                            member.AcademicOnlineAuthorId.Trim(),
                        Name = member.DisplayName,
                        Email = member.ExpectedEmail
                    };
            }
            else
            {
                var resolution =
                    await ResolveRosterMemberAsync(
                        member,
                        cancellationToken);

                resolved = resolution.Candidate;
                resolutionMethod =
                    resolution.ResolutionMethod;
                resolutionDiagnostic =
                    resolution.Diagnostic;
                searchRequests +=
                    resolution.SearchRequests;
            }

            if (resolved is not null)
            {
                scholarsById[
                    resolved.AuthorId] =
                    resolved;

                rosterSources.Add(
                    new RosterPublicationSource(
                        member.RosterMemberId,
                        member.DisplayName,
                        "AcademicOnline",
                        resolved.AuthorId));

                await _repository
                    .RecordRosterMemberResolutionAsync(
                        member.RosterMemberId,
                        resolved.AuthorId,
                        status: "Resolved",
                        errorMessage: null,
                        resolutionMethod:
                            resolutionMethod,
                        resolutionEvidence:
                            resolutionDiagnostic,
                        cancellationToken:
                            CancellationToken.None);

                continue;
            }

            string? openAlexAuthorId =
                string.IsNullOrWhiteSpace(
                    member.OpenAlexAuthorId)
                    ? null
                    : member.OpenAlexAuthorId.Trim();

            var openAlexStatus =
                member.OpenAlexResolutionStatus;

            if (string.IsNullOrWhiteSpace(
                    openAlexAuthorId) ||
                !IsUsableOpenAlexFallbackStatus(
                    openAlexStatus))
            {
                var openAlexPreview =
                    await PreviewOpenAlexRosterFallbackAsync(
                        member,
                        cancellationToken);

                await _repository
                    .RecordRosterMemberOpenAlexFallbackAsync(
                        member.RosterMemberId,
                        openAlexPreview.OpenAlexAuthorId,
                        openAlexPreview.Status,
                        openAlexPreview.Evidence,
                        CancellationToken.None);

                openAlexAuthorId =
                    openAlexPreview.OpenAlexAuthorId;
                openAlexStatus =
                    openAlexPreview.Status;

                if (!IsUsableOpenAlexFallbackStatus(
                        openAlexStatus) ||
                    string.IsNullOrWhiteSpace(
                        openAlexAuthorId))
                {
                    var seedResolution =
                        await ResolveOpenAlexRosterIdentityFromSeedsAsync(
                            member,
                            cancellationToken);

                    if (!string.Equals(
                            seedResolution.Status,
                            "NoSeeds",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await _repository
                            .RecordRosterMemberOpenAlexFallbackAsync(
                                member.RosterMemberId,
                                seedResolution.OpenAlexAuthorId,
                                seedResolution.Status,
                                seedResolution.Evidence,
                                CancellationToken.None);

                        openAlexAuthorId =
                            seedResolution.OpenAlexAuthorId;
                        openAlexStatus =
                            seedResolution.Status;
                    }
                }
            }

            if (IsUsableOpenAlexFallbackStatus(
                    openAlexStatus) &&
                !string.IsNullOrWhiteSpace(
                    openAlexAuthorId) &&
                !verifiedOnly)
            {
                rosterSources.Add(
                    new RosterPublicationSource(
                        member.RosterMemberId,
                        member.DisplayName,
                        "OpenAlex",
                        openAlexAuthorId));

                await _repository
                    .RecordRosterMemberResolutionAsync(
                        member.RosterMemberId,
                        academicOnlineAuthorId: null,
                        status: "Unresolved",
                        errorMessage:
                            resolutionDiagnostic
                            ?? "Academic Online identity unresolved; using validated OpenAlex fallback.",
                        resolutionMethod:
                            resolutionMethod,
                        resolutionEvidence:
                            resolutionDiagnostic,
                        cancellationToken:
                            CancellationToken.None);

                continue;
            }

            unresolvedMembers.Add(
                member.DisplayName);

            await _repository
                .RecordRosterMemberResolutionAsync(
                    member.RosterMemberId,
                    academicOnlineAuthorId: null,
                    status: "Unresolved",
                    errorMessage:
                        verifiedOnly &&
                        !string.IsNullOrWhiteSpace(
                            openAlexAuthorId)
                            ? "OpenAlex fallback cannot satisfy Academic Online VerifiedOnly policy."
                            : resolutionDiagnostic
                              ?? "No unique publication-source identity matched the official roster member.",
                    resolutionMethod:
                        resolutionMethod,
                    resolutionEvidence:
                        resolutionDiagnostic,
                    cancellationToken:
                        CancellationToken.None);
        }

        var openAlexFallbackCount =
            rosterSources.Count(item =>
                string.Equals(
                    item.SourceProvider,
                    "OpenAlex",
                    StringComparison.OrdinalIgnoreCase));

        var academicOnlineCount =
            rosterSources.Count(item =>
                string.Equals(
                    item.SourceProvider,
                    "AcademicOnline",
                    StringComparison.OrdinalIgnoreCase));

        return new ScholarSelection
        {
            SourceMode = "FacultyRoster",
            Scholars =
                scholarsById.Values
                    .OrderBy(
                        item =>
                            item.Name
                            ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            RosterPublicationSources =
                rosterSources,
            ScholarsDiscovered =
                roster.Count,
            ScholarPagesFetched =
                searchRequests,
            ScholarPaginationSafeguardHit =
                false,
            SourceMembershipComplete =
                rosterSources
                    .Select(item =>
                        item.RosterMemberId)
                    .Distinct()
                    .Count() ==
                roster.Count,
            SourceDetail =
                $"Roster sources={rosterSources.Select(item => item.RosterMemberId).Distinct().Count()}/{roster.Count}; " +
                $"AcademicOnline={academicOnlineCount}; " +
                $"OpenAlexFallback={openAlexFallbackCount}; " +
                $"VerifiedOnly={verifiedOnly}; " +
                $"Unresolved={string.Join(",", unresolvedMembers)}."
        };
    }

    private async Task<RosterResolution> ResolveRosterMemberAsync(
        AcademicOnlineDepartmentRosterMember member,
        CancellationToken cancellationToken)
    {
        var searchRequests = 0;
        var candidatesById =
            new Dictionary<string, AcademicOnlineScholarSearchCandidate>(
                StringComparer.OrdinalIgnoreCase);
        var searchNotes = new List<string>();

        foreach (var searchTerm in BuildRosterSearchTerms(member))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidates =
                await _client.SearchScholarCandidatesAsync(
                    searchTerm,
                    departmentName: null,
                    rowsPerPage: 25,
                    cancellationToken: cancellationToken);

            searchRequests++;

            searchNotes.Add(
                $"Search={searchTerm};Count={candidates.Count}");

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate.AuthorId))
                {
                    candidatesById[candidate.AuthorId] = candidate;
                }
            }
        }

        var expectedEmail =
            string.IsNullOrWhiteSpace(member.ExpectedEmail)
                ? null
                : member.ExpectedEmail.Trim();

        if (!string.IsNullOrWhiteSpace(expectedEmail))
        {
            var exactEmailMatches =
                candidatesById.Values
                    .Where(candidate =>
                        string.Equals(
                            candidate.Email?.Trim(),
                            expectedEmail,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (exactEmailMatches.Count == 1)
            {
                return new RosterResolution(
                    exactEmailMatches[0],
                    searchRequests,
                    ResolutionMethod: "ExactUvaEmail",
                    Diagnostic:
                        "Resolved by exact UVA email returned by Academic Online scholar search.");
            }
        }

        var searchResolution =
            await EvaluateSearchCandidatesAsync(
                member,
                candidatesById.Values.ToList(),
                expectedEmail,
                searchRequests,
                searchNotes,
                cancellationToken);

        if (searchResolution.Candidate is not null)
        {
            return searchResolution;
        }

        var coauthorResolution =
            await ResolveRosterMemberFromCoauthorCacheAsync(
                member,
                searchRequests,
                cancellationToken);

        if (coauthorResolution.Candidate is not null)
        {
            return coauthorResolution;
        }

        var combinedDiagnostic =
            string.Join(
                " || ",
                new[]
                {
                    searchResolution.Diagnostic,
                    coauthorResolution.Diagnostic
                }
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item)));

        return new RosterResolution(
            Candidate: null,
            SearchRequests: searchRequests,
            ResolutionMethod: "ScholarSearch+CoauthorCache",
            Diagnostic:
                string.IsNullOrWhiteSpace(combinedDiagnostic)
                    ? "No unique Academic Online scholar identity matched the official roster member."
                    : combinedDiagnostic);
    }

    private async Task<RosterResolution> EvaluateSearchCandidatesAsync(
        AcademicOnlineDepartmentRosterMember member,
        IReadOnlyList<AcademicOnlineScholarSearchCandidate> candidates,
        string? expectedEmail,
        int searchRequests,
        IReadOnlyList<string> searchNotes,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return new RosterResolution(
                Candidate: null,
                SearchRequests: searchRequests,
                ResolutionMethod: "ScholarSearch",
                Diagnostic:
                    $"No Academic Online candidates returned. {string.Join(" | ", searchNotes)}");
        }

        var candidatesToEvaluate =
            candidates
                .Select(candidate =>
                    new
                    {
                        Candidate = candidate,
                        NameScore =
                            CalculateRosterNameScore(
                                candidate.Name,
                                member.DisplayName)
                    })
                .Where(item =>
                    item.NameScore >= 70 ||
                    (!string.IsNullOrWhiteSpace(expectedEmail) &&
                     string.Equals(
                         item.Candidate.Email?.Trim(),
                         expectedEmail,
                         StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(item =>
                    item.NameScore)
                .ThenByDescending(item =>
                    item.Candidate.RankingScore ?? 0m)
                .Take(10)
                .ToList();

        if (candidatesToEvaluate.Count == 0)
        {
            return new RosterResolution(
                Candidate: null,
                SearchRequests: searchRequests,
                ResolutionMethod: "ScholarSearch",
                Diagnostic:
                    $"Candidates returned, but none passed name compatibility. " +
                    $"Roster={member.DisplayName}; " +
                    $"Candidates={string.Join(",", candidates.Select(item => item.Name ?? item.AuthorId))}.");
        }

        var evaluations =
            new List<RosterCandidateEvaluation>();

        var evaluationNotes =
            searchNotes.ToList();

        foreach (var item in candidatesToEvaluate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = item.Candidate;

            try
            {
                using var profile =
                    await _client.GetAuthorGeneralAsync(
                        candidate.AuthorId,
                        cancellationToken);

                var evaluation =
                    BuildRosterCandidateEvaluation(
                        member,
                        candidate,
                        candidate.Name,
                        expectedEmail,
                        item.NameScore,
                        profile.RootElement);

                evaluations.Add(evaluation);
            }
            catch (HttpRequestException ex)
            {
                evaluationNotes.Add(
                    $"ProfileError={candidate.AuthorId}:{ex.Message}");
            }
            catch (TaskCanceledException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                evaluationNotes.Add(
                    $"ProfileTimeout={candidate.AuthorId}:{ex.Message}");
            }
        }

        return SelectDefensibleRosterCandidate(
            evaluations,
            searchRequests,
            "ScholarSearchProfile",
            $"Scholar search evidence. {string.Join(" | ", evaluationNotes)}");
    }

    private async Task<RosterResolution>
        ResolveRosterMemberFromCoauthorCacheAsync(
            AcademicOnlineDepartmentRosterMember member,
            int searchRequests,
            CancellationToken cancellationToken)
    {
        var candidatesById =
            new Dictionary<string, AcademicOnlineCoauthorIdentityCandidate>(
                StringComparer.OrdinalIgnoreCase);

        var evidenceNotes =
            new List<string>();

        foreach (var pair in BuildCoauthorTokenPairs(
                     member.DisplayName))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidates =
                await _repository.FindCoauthorIdentityCandidatesAsync(
                    pair.Token1,
                    pair.Token2,
                    cancellationToken);

            evidenceNotes.Add(
                $"CoauthorSearch={pair.Token1}+{pair.Token2};Count={candidates.Count}");

            foreach (var candidate in candidates)
            {
                if (!candidatesById.TryGetValue(
                        candidate.AcademicOnlineAuthorId,
                        out var existing)
                    ||
                    candidate.EvidencePublicationCount >
                    existing.EvidencePublicationCount)
                {
                    candidatesById[
                        candidate.AcademicOnlineAuthorId] =
                        candidate;
                }
            }
        }

        if (candidatesById.Count == 0)
        {
            return new RosterResolution(
                Candidate: null,
                SearchRequests: searchRequests,
                ResolutionMethod: "CoauthorCache",
                Diagnostic:
                    $"No non-null Academic Online coauthor IDs were found in the local publication cache. " +
                    $"{string.Join(" | ", evidenceNotes)}");
        }

        var evaluations =
            new List<RosterCandidateEvaluation>();

        foreach (var localCandidate in
                 candidatesById.Values
                     .OrderByDescending(item =>
                         item.EvidencePublicationCount)
                     .ThenBy(item =>
                         item.CoauthorName,
                         StringComparer.OrdinalIgnoreCase)
                     .Take(10))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var profile =
                    await _client.GetAuthorGeneralAsync(
                        localCandidate.AcademicOnlineAuthorId,
                        cancellationToken);

                var root = profile.RootElement;

                var profileHasIdentityEvidence =
                    !string.IsNullOrWhiteSpace(
                        GetString(root, "name"))
                    ||
                    !string.IsNullOrWhiteSpace(
                        GetString(root, "email"))
                    ||
                    !string.IsNullOrWhiteSpace(
                        GetString(root, "institution"))
                    ||
                    AcademicOnlineDepartmentParser
                        .GetDepartmentAffiliations(root)
                        .Count > 0;

                if (!profileHasIdentityEvidence)
                {
                    evidenceNotes.Add(
                        $"RejectedCoauthorId={localCandidate.AcademicOnlineAuthorId};" +
                        $"Name={localCandidate.CoauthorName};" +
                        $"EvidencePublications={localCandidate.EvidencePublicationCount};" +
                        "Reason=UVA author/general returned no identity evidence.");

                    continue;
                }

                var candidate =
                    new AcademicOnlineScholarSearchCandidate
                    {
                        AuthorId =
                            localCandidate.AcademicOnlineAuthorId,
                        Name =
                            localCandidate.CoauthorName
                    };

                var coauthorNameScore =
                    CalculateRosterNameScore(
                        localCandidate.CoauthorName,
                        member.DisplayName);

                var evaluation =
                    BuildRosterCandidateEvaluation(
                        member,
                        candidate,
                        localCandidate.CoauthorName,
                        member.ExpectedEmail,
                        coauthorNameScore,
                        root);

                evaluation =
                    evaluation with
                    {
                        Score =
                            evaluation.Score +
                            Math.Min(
                                20,
                                localCandidate
                                    .EvidencePublicationCount * 2),
                        AffiliationEvidence =
                            evaluation.AffiliationEvidence +
                            $";CoauthorPublications={localCandidate.EvidencePublicationCount};" +
                            $"CoauthorYears={localCandidate.EarliestPublicationYear?.ToString() ?? "?"}-" +
                            $"{localCandidate.LatestPublicationYear?.ToString() ?? "?"}"
                    };

                evaluations.Add(evaluation);
            }
            catch (HttpRequestException ex)
            {
                evidenceNotes.Add(
                    $"RejectedCoauthorId={localCandidate.AcademicOnlineAuthorId};" +
                    $"Name={localCandidate.CoauthorName};" +
                    $"Reason=Profile request failed: {ex.Message}");
            }
            catch (TaskCanceledException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                evidenceNotes.Add(
                    $"RejectedCoauthorId={localCandidate.AcademicOnlineAuthorId};" +
                    $"Name={localCandidate.CoauthorName};" +
                    $"Reason=Profile request timed out: {ex.Message}");
            }
        }

        return SelectDefensibleRosterCandidate(
            evaluations,
            searchRequests,
            "VerifiedCoauthorCache",
            $"Whole-cache coauthor evidence. {string.Join(" | ", evidenceNotes)}");
    }

    private static RosterCandidateEvaluation
        BuildRosterCandidateEvaluation(
            AcademicOnlineDepartmentRosterMember member,
            AcademicOnlineScholarSearchCandidate candidate,
            string? evidenceName,
            string? expectedEmail,
            int initialNameScore,
            JsonElement root)
    {
        var profileName =
            GetString(root, "name")
            ?? candidate.Name;

        var profileEmail =
            GetString(root, "email")
            ?? candidate.Email;

        var profileInstitution =
            GetString(root, "institution")
            ?? candidate.Institution;

        var profilePosition =
            GetString(root, "position")
            ?? candidate.Position;

        var nameScore =
            Math.Max(
                initialNameScore,
                Math.Max(
                    CalculateRosterNameScore(
                        profileName,
                        member.DisplayName),
                    CalculateRosterNameScore(
                        evidenceName,
                        member.DisplayName)));

        var exactEmail =
            !string.IsNullOrWhiteSpace(expectedEmail) &&
            string.Equals(
                profileEmail?.Trim(),
                expectedEmail.Trim(),
                StringComparison.OrdinalIgnoreCase);

        var affiliations =
            AcademicOnlineDepartmentParser
                .GetDepartmentAffiliations(root);

        var uvaEvidence =
            exactEmail ||
            IsUvaEmail(profileEmail) ||
            IsUvaInstitution(profileInstitution) ||
            affiliations.Any(
                IsUvaSchoolOfMedicineAffiliation);

        var neurosurgeryEvidence =
            ContainsNeurosurgeryTerm(profilePosition) ||
            affiliations.Any(
                IsNeurosurgeryAffiliation);

        var score =
            nameScore +
            (exactEmail ? 300 : 0) +
            (uvaEvidence ? 100 : 0) +
            (neurosurgeryEvidence ? 25 : 0);

        return new RosterCandidateEvaluation(
            candidate,
            profileName,
            profileEmail,
            profileInstitution,
            nameScore,
            exactEmail,
            uvaEvidence,
            neurosurgeryEvidence,
            score,
            BuildAffiliationEvidence(affiliations));
    }

    private static RosterResolution
        SelectDefensibleRosterCandidate(
            IReadOnlyList<RosterCandidateEvaluation> evaluations,
            int searchRequests,
            string resolutionMethod,
            string diagnosticPrefix)
    {
        var defensible =
            evaluations
                .Where(item =>
                    item.NameScore >= 70 &&
                    item.UvaEvidence)
                .OrderByDescending(item =>
                    item.Score)
                .ThenByDescending(item =>
                    item.Candidate.RankingScore ?? 0m)
                .ToList();

        if (defensible.Count == 1)
        {
            return new RosterResolution(
                defensible[0].Candidate,
                searchRequests,
                ResolutionMethod:
                    resolutionMethod,
                Diagnostic:
                    $"{diagnosticPrefix} Resolved uniquely. " +
                    $"{FormatRosterEvaluation(defensible[0])}");
        }

        if (defensible.Count > 1)
        {
            var top = defensible[0];
            var second = defensible[1];

            if (top.ExactExpectedEmail &&
                !second.ExactExpectedEmail)
            {
                return new RosterResolution(
                    top.Candidate,
                    searchRequests,
                    ResolutionMethod:
                        resolutionMethod,
                    Diagnostic:
                        $"{diagnosticPrefix} " +
                        $"Resolved by exact-email tie-breaker. " +
                        $"{FormatRosterEvaluation(top)}");
            }

            if (top.Score - second.Score >= 25)
            {
                return new RosterResolution(
                    top.Candidate,
                    searchRequests,
                    ResolutionMethod:
                        resolutionMethod,
                    Diagnostic:
                        $"{diagnosticPrefix} " +
                        $"Resolved by unique evidence score. " +
                        $"{FormatRosterEvaluation(top)}");
            }
        }

        var evaluationText =
            evaluations.Count == 0
                ? "(no profile-validated candidates)"
                : string.Join(
                    " || ",
                    evaluations.Select(
                        FormatRosterEvaluation));

        return new RosterResolution(
            Candidate: null,
            SearchRequests: searchRequests,
            ResolutionMethod:
                resolutionMethod,
            Diagnostic:
                $"{diagnosticPrefix} " +
                $"No unique profile-validated UVA identity. " +
                $"Evaluations={evaluationText}");
    }

    private static bool IsUsableOpenAlexFallbackStatus(
        string? status)
    {
        return string.Equals(
                   status,
                   "ProposedUnique",
                   StringComparison.OrdinalIgnoreCase)
               ||
               string.Equals(
                   status,
                   "DoiSeedConverged",
                   StringComparison.OrdinalIgnoreCase);
    }

    private async Task<OpenAlexFallbackPreview>
        ResolveOpenAlexRosterIdentityFromSeedsAsync(
            AcademicOnlineDepartmentRosterMember member,
            CancellationToken cancellationToken)
    {
        var seeds =
            await _repository.GetRosterIdentitySeedsAsync(
                member.RosterMemberId,
                cancellationToken);

        var doiSeeds =
            seeds
                .Where(seed =>
                    string.Equals(
                        seed.SeedType,
                        "DOI",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(
                        seed.SeedValue) &&
                    !string.IsNullOrWhiteSpace(
                        seed.OpenAlexWorkId))
                .ToList();

        if (doiSeeds.Count < 2)
        {
            return new OpenAlexFallbackPreview(
                OpenAlexAuthorId: null,
                Status: "NoSeeds",
                Evidence:
                    $"DOI-seeded recovery requires at least two active DOI/OpenAlex-work seeds. " +
                    $"Roster={member.DisplayName}; SeedCount={doiSeeds.Count}.");
        }

        var evidence =
            new List<OpenAlexSeedAuthorEvidence>();

        var notes =
            new List<string>();

        foreach (var seed in doiSeeds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var workResponse =
                await _openAlexClient.GetWorkByIdAsync(
                    seed.OpenAlexWorkId!,
                    cancellationToken);

            if (!workResponse.IsSuccessStatusCode ||
                workResponse.RawDocument is null)
            {
                notes.Add(
                    $"Seed={seed.SeedValue};" +
                    $"Work={seed.OpenAlexWorkId};" +
                    $"Status=WorkLookupFailed;" +
                    $"Http={workResponse.HttpStatusCode}");

                continue;
            }

            var work =
                workResponse.RawDocument.RootElement;

            var expectedDoi =
                NormalizeDoiSeed(
                    seed.SeedValue);

            var actualDoi =
                NormalizeDoiSeed(
                    GetString(
                        work,
                        "doi"));

            if (!string.Equals(
                    expectedDoi,
                    actualDoi,
                    StringComparison.OrdinalIgnoreCase))
            {
                notes.Add(
                    $"Seed={seed.SeedValue};" +
                    $"Work={seed.OpenAlexWorkId};" +
                    $"Status=DoiMismatch;" +
                    $"ActualDoi={actualDoi ?? "(null)"}");

                continue;
            }

            if (!work.TryGetProperty(
                    "authorships",
                    out var authorships) ||
                authorships.ValueKind !=
                    JsonValueKind.Array)
            {
                notes.Add(
                    $"Seed={seed.SeedValue};" +
                    $"Work={seed.OpenAlexWorkId};" +
                    "Status=NoAuthorships");

                continue;
            }

            var matches =
                new List<OpenAlexSeedAuthorEvidence>();

            foreach (var authorship in
                     authorships.EnumerateArray())
            {
                if (authorship.ValueKind !=
                        JsonValueKind.Object ||
                    !authorship.TryGetProperty(
                        "author",
                        out var authorshipAuthor) ||
                    authorshipAuthor.ValueKind !=
                        JsonValueKind.Object)
                {
                    continue;
                }

                var authorId =
                    NormalizeOpenAlexAuthorId(
                        GetString(
                            authorshipAuthor,
                            "id"));

                var authorName =
                    GetString(
                        authorshipAuthor,
                        "display_name");

                var nameScore =
                    CalculateRosterNameScore(
                        authorName,
                        member.DisplayName);

                if (string.IsNullOrWhiteSpace(
                        authorId) ||
                    nameScore < 85)
                {
                    continue;
                }

                matches.Add(
                    new OpenAlexSeedAuthorEvidence(
                        seed.IdentitySeedId,
                        seed.SeedValue,
                        seed.OpenAlexWorkId!,
                        authorId,
                        authorName,
                        nameScore,
                        seed.SourceReference));
            }

            if (matches.Count == 1)
            {
                evidence.Add(
                    matches[0]);

                notes.Add(
                    $"Seed={seed.SeedValue};" +
                    $"Work={seed.OpenAlexWorkId};" +
                    $"MatchedAuthor={matches[0].OpenAlexAuthorId};" +
                    $"MatchedName={matches[0].AuthorName ?? "(null)"};" +
                    $"NameScore={matches[0].NameScore}");
            }
            else
            {
                notes.Add(
                    $"Seed={seed.SeedValue};" +
                    $"Work={seed.OpenAlexWorkId};" +
                    $"Status={(matches.Count == 0 ? "NoStrongAuthorMatch" : "AmbiguousAuthorMatch")};" +
                    $"MatchCount={matches.Count}");
            }
        }

        if (evidence.Count < 2)
        {
            return new OpenAlexFallbackPreview(
                OpenAlexAuthorId: null,
                Status: "InsufficientSeedEvidence",
                Evidence:
                    $"DOI-seeded recovery did not produce at least two independently matching works. " +
                    $"Roster={member.DisplayName}; " +
                    $"MatchedSeeds={evidence.Count}/{doiSeeds.Count}; " +
                    $"{string.Join(" | ", notes)}");
        }

        var byAuthor =
            evidence
                .GroupBy(
                    item =>
                        item.OpenAlexAuthorId,
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(
                    group =>
                        group.Count())
                .ToList();

        if (byAuthor.Count != 1)
        {
            return new OpenAlexFallbackPreview(
                OpenAlexAuthorId: null,
                Status: "AmbiguousSeedEvidence",
                Evidence:
                    $"DOI seeds resolved to more than one OpenAlex author ID. " +
                    $"Roster={member.DisplayName}; " +
                    $"Authors={string.Join(",", byAuthor.Select(group => $"{group.Key}:{group.Count()}"))}; " +
                    $"{string.Join(" | ", notes)}");
        }

        var winning =
            byAuthor[0].ToList();

        var candidateAuthorId =
            winning[0].OpenAlexAuthorId;

        if (winning.Count < 2)
        {
            return new OpenAlexFallbackPreview(
                OpenAlexAuthorId: null,
                Status: "InsufficientSeedEvidence",
                Evidence:
                    $"Only one DOI seed supported OpenAlex author {candidateAuthorId}. " +
                    $"{string.Join(" | ", notes)}");
        }

        using var authorResponse =
            await _openAlexClient.GetAuthorByIdAsync(
                candidateAuthorId,
                cancellationToken);

        if (!authorResponse.IsSuccessStatusCode ||
            authorResponse.RawDocument is null)
        {
            return new OpenAlexFallbackPreview(
                OpenAlexAuthorId: null,
                Status: "SeedAuthorValidationUnavailable",
                Evidence:
                    $"DOI seeds converged on {candidateAuthorId}, but direct OpenAlex author validation failed. " +
                    $"Http={authorResponse.HttpStatusCode}; " +
                    $"{string.Join(" | ", notes)}");
        }

        var author =
            authorResponse.RawDocument.RootElement;

        var profileName =
            GetString(
                author,
                "display_name");

        var profileNameScore =
            CalculateRosterNameScore(
                profileName,
                member.DisplayName);

        if (profileNameScore < 85)
        {
            return new OpenAlexFallbackPreview(
                OpenAlexAuthorId: null,
                Status: "SeedAuthorNameMismatch",
                Evidence:
                    $"DOI seeds converged on {candidateAuthorId}, but the direct author profile name was not compatible. " +
                    $"Roster={member.DisplayName}; ProfileName={profileName ?? "(null)"}; " +
                    $"NameScore={profileNameScore}; " +
                    $"{string.Join(" | ", notes)}");
        }

        var uvaEvidence =
            GetOpenAlexUvaEvidence(
                author);

        var acceptanceBasis =
            uvaEvidence.IsUva
                ? "Multi-work DOI convergence + direct OpenAlex author name + UVA affiliation."
                : winning.Count >= 3
                    ? "Three independent DOI seeds + direct OpenAlex author name; current UVA membership is supplied by the official roster."
                    : string.Empty;

        if (string.IsNullOrWhiteSpace(
                acceptanceBasis))
        {
            return new OpenAlexFallbackPreview(
                OpenAlexAuthorId: null,
                Status: "SeedConvergedNoCurrentUvaEvidence",
                Evidence:
                    $"Two DOI seeds converged on {candidateAuthorId}, but the OpenAlex author entity did not provide current UVA affiliation evidence. " +
                    $"Roster={member.DisplayName}; ProfileName={profileName}; " +
                    $"NameScore={profileNameScore}; " +
                    $"UvaEvidence={uvaEvidence.Evidence}; " +
                    $"{string.Join(" | ", notes)}");
        }

        return new OpenAlexFallbackPreview(
            OpenAlexAuthorId:
                candidateAuthorId,
            Status:
                "DoiSeedConverged",
            Evidence:
                $"DOI-seeded identity recovery succeeded. " +
                $"Roster={member.DisplayName}; " +
                $"OpenAlexAuthorId={candidateAuthorId}; " +
                $"ProfileName={profileName}; " +
                $"ProfileNameScore={profileNameScore}; " +
                $"SupportingSeeds={winning.Count}/{doiSeeds.Count}; " +
                $"UvaAffiliation={uvaEvidence.IsUva}; " +
                $"UvaEvidence={uvaEvidence.Evidence}; " +
                $"AcceptanceBasis={acceptanceBasis} " +
                $"{string.Join(" | ", notes)}");
    }

    private static string? NormalizeDoiSeed(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        var normalized =
            value.Trim();

        normalized =
            normalized
                .Replace(
                    "https://doi.org/",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    "http://doi.org/",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase);

        if (normalized.StartsWith(
                "doi:",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized =
                normalized[4..];
        }

        normalized =
            normalized.Trim();

        return normalized.Length == 0
            ? null
            : normalized.ToLowerInvariant();
    }

    private async Task<OpenAlexFallbackPreview>
        PreviewOpenAlexRosterFallbackAsync(
            AcademicOnlineDepartmentRosterMember member,
            CancellationToken cancellationToken)
    {
        var candidatesById =
            new Dictionary<string, OpenAlexFallbackCandidate>(
                StringComparer.OrdinalIgnoreCase);

        var notes =
            new List<string>();

        foreach (var searchTerm in
                 BuildOpenAlexSearchTerms(
                     member.DisplayName))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var page =
                    await _openAlexClient
                        .GetAuthorsSearchPageAsync(
                            searchTerm,
                            perPage: 25,
                            cancellationToken:
                                cancellationToken);

                notes.Add(
                    $"Search={searchTerm};" +
                    $"Results={page.ResultsCount}");

                foreach (var item in
                         page.ResultsElement
                             .EnumerateArray())
                {
                    var id =
                        NormalizeOpenAlexAuthorId(
                            GetString(item, "id"));

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var displayName =
                        GetString(
                            item,
                            "display_name");

                    var nameScore =
                        CalculateRosterNameScore(
                            displayName,
                            member.DisplayName);

                    var uvaEvidence =
                        GetOpenAlexUvaEvidence(
                            item);

                    candidatesById[id] =
                        new OpenAlexFallbackCandidate(
                            id,
                            displayName,
                            GetString(item, "orcid"),
                            nameScore,
                            uvaEvidence.IsUva,
                            uvaEvidence.Evidence,
                            GetInt(item, "works_count"),
                            GetInt(item, "cited_by_count"));
                }
            }
            catch (HttpRequestException ex)
            {
                return new OpenAlexFallbackPreview(
                    OpenAlexAuthorId: null,
                    Status: "Unavailable",
                    Evidence:
                        $"OpenAlex author search failed. " +
                        $"Search={searchTerm}; " +
                        $"Error={ex.Message}");
            }
            catch (TaskCanceledException ex)
                when (!cancellationToken
                    .IsCancellationRequested)
            {
                return new OpenAlexFallbackPreview(
                    OpenAlexAuthorId: null,
                    Status: "Unavailable",
                    Evidence:
                        $"OpenAlex author search timed out. " +
                        $"Search={searchTerm}; " +
                        $"Error={ex.Message}");
            }
        }

        var defensible =
            candidatesById.Values
                .Where(candidate =>
                    candidate.NameScore >= 85 &&
                    candidate.HasUvaAffiliation)
                .OrderByDescending(candidate =>
                    candidate.NameScore)
                .ThenByDescending(candidate =>
                    candidate.WorksCount ?? 0)
                .ToList();

        if (defensible.Count == 1)
        {
            var candidate =
                defensible[0];

            return new OpenAlexFallbackPreview(
                candidate.OpenAlexAuthorId,
                "ProposedUnique",
                $"OpenAlex preview only; not used for publication sync. " +
                $"Roster={member.DisplayName}; " +
                $"{FormatOpenAlexFallbackCandidate(candidate)}; " +
                $"{string.Join(" | ", notes)}");
        }

        if (defensible.Count > 1)
        {
            return new OpenAlexFallbackPreview(
                OpenAlexAuthorId: null,
                Status: "Ambiguous",
                Evidence:
                    $"Multiple UVA-affiliated OpenAlex author candidates passed the conservative threshold. " +
                    $"Roster={member.DisplayName}; " +
                    $"Candidates={string.Join(" || ", defensible.Take(5).Select(FormatOpenAlexFallbackCandidate))}; " +
                    $"{string.Join(" | ", notes)}");
        }

        var strongest =
            candidatesById.Values
                .OrderByDescending(candidate =>
                    candidate.NameScore)
                .ThenByDescending(candidate =>
                    candidate.HasUvaAffiliation)
                .ThenByDescending(candidate =>
                    candidate.WorksCount ?? 0)
                .Take(5)
                .ToList();

        return new OpenAlexFallbackPreview(
            OpenAlexAuthorId: null,
            Status: "NoMatch",
            Evidence:
                $"No unique UVA-affiliated OpenAlex author passed the conservative identity threshold. " +
                $"Roster={member.DisplayName}; " +
                $"Candidates={(strongest.Count == 0 ? "(none)" : string.Join(" || ", strongest.Select(FormatOpenAlexFallbackCandidate)))}; " +
                $"{string.Join(" | ", notes)}");
    }

    private static IReadOnlyList<string>
        BuildOpenAlexSearchTerms(
            string displayName)
    {
        var tokens =
            TokenizePersonName(displayName)
                .Where(token =>
                    token.Length > 1)
                .ToList();

        var terms =
            new List<string>
            {
                displayName.Trim()
            };

        if (tokens.Count >= 2)
        {
            terms.Add(
                tokens[0] +
                " " +
                tokens[^1]);
        }

        if (tokens.Count >= 3)
        {
            terms.Add(
                tokens[0] +
                " " +
                tokens[1]);

            terms.Add(
                tokens[1] +
                " " +
                tokens[^1]);
        }

        return terms
            .Where(term =>
                !string.IsNullOrWhiteSpace(term))
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
    }

    private OpenAlexUvaEvidence
        GetOpenAlexUvaEvidence(
            JsonElement author)
    {
        var configuredIds =
            _openAlexOptions.InstitutionIds
                .Select(NormalizeOpenAlexInstitutionId)
                .Where(id =>
                    !string.IsNullOrWhiteSpace(id))
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var evidence =
            new List<string>();

        if (author.TryGetProperty(
                "last_known_institutions",
                out var lastKnown) &&
            lastKnown.ValueKind ==
                JsonValueKind.Array)
        {
            foreach (var institution in
                     lastKnown.EnumerateArray())
            {
                var institutionId =
                    NormalizeOpenAlexInstitutionId(
                        GetString(
                            institution,
                            "id"));

                var institutionName =
                    GetString(
                        institution,
                        "display_name");

                if (IsConfiguredUvaInstitution(
                        institutionId,
                        institutionName,
                        configuredIds))
                {
                    evidence.Add(
                        $"LastKnown={institutionName ?? institutionId}");
                }
            }
        }

        if (author.TryGetProperty(
                "affiliations",
                out var affiliations) &&
            affiliations.ValueKind ==
                JsonValueKind.Array)
        {
            foreach (var affiliation in
                     affiliations.EnumerateArray())
            {
                if (!affiliation.TryGetProperty(
                        "institution",
                        out var institution) ||
                    institution.ValueKind !=
                        JsonValueKind.Object)
                {
                    continue;
                }

                var institutionId =
                    NormalizeOpenAlexInstitutionId(
                        GetString(
                            institution,
                            "id"));

                var institutionName =
                    GetString(
                        institution,
                        "display_name");

                if (IsConfiguredUvaInstitution(
                        institutionId,
                        institutionName,
                        configuredIds))
                {
                    evidence.Add(
                        $"Affiliation={institutionName ?? institutionId}");
                }
            }
        }

        return new OpenAlexUvaEvidence(
            evidence.Count > 0,
            evidence.Count == 0
                ? "(none)"
                : string.Join(
                    ";",
                    evidence.Distinct(
                        StringComparer.OrdinalIgnoreCase)));
    }

    private static bool IsConfiguredUvaInstitution(
        string? institutionId,
        string? institutionName,
        IReadOnlySet<string> configuredIds)
    {
        if (!string.IsNullOrWhiteSpace(
                institutionId) &&
            configuredIds.Contains(
                institutionId))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(
                institutionName))
        {
            return false;
        }

        return institutionName.Contains(
                   "University of Virginia",
                   StringComparison.OrdinalIgnoreCase)
               ||
               institutionName.Contains(
                   "UVA Health",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string?
        NormalizeOpenAlexInstitutionId(
            string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed =
            value.Trim().TrimEnd('/');

        var slashIndex =
            trimmed.LastIndexOf('/');

        return (
            slashIndex >= 0
                ? trimmed[(slashIndex + 1)..]
                : trimmed)
            .ToUpperInvariant();
    }

    private static string?
        NormalizeOpenAlexAuthorId(
            string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed =
            value.Trim().TrimEnd('/');

        var slashIndex =
            trimmed.LastIndexOf('/');

        var id =
            slashIndex >= 0
                ? trimmed[(slashIndex + 1)..]
                : trimmed;

        return id.StartsWith(
                "A",
                StringComparison.OrdinalIgnoreCase)
            ? id.ToUpperInvariant()
            : null;
    }

    private static string
        FormatOpenAlexFallbackCandidate(
            OpenAlexFallbackCandidate candidate)
    {
        return
            $"OpenAlexAuthorId={candidate.OpenAlexAuthorId};" +
            $"Name={candidate.DisplayName ?? "(null)"};" +
            $"Orcid={candidate.Orcid ?? "(null)"};" +
            $"NameScore={candidate.NameScore};" +
            $"UvaAffiliation={candidate.HasUvaAffiliation};" +
            $"UvaEvidence={candidate.UvaEvidence};" +
            $"Works={candidate.WorksCount?.ToString() ?? "(null)"};" +
            $"Citations={candidate.CitedByCount?.ToString() ?? "(null)"}";
    }

    private static IReadOnlyList<string>
        BuildRosterSearchTerms(
            AcademicOnlineDepartmentRosterMember member)
    {
        var terms =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
                member.ExpectedEmail))
        {
            var expectedEmail =
                member.ExpectedEmail.Trim();

            terms.Add(expectedEmail);

            var atIndex =
                expectedEmail.IndexOf('@');

            if (atIndex > 0)
            {
                terms.Add(
                    expectedEmail[..atIndex]);
            }
        }

        terms.Add(
            member.DisplayName.Trim());

        var significantTokens =
            TokenizePersonName(member.DisplayName)
                .Where(token =>
                    token.Length > 1)
                .ToList();

        if (significantTokens.Count >= 2)
        {
            terms.Add(
                string.Join(
                    " ",
                    significantTokens));

            terms.Add(
                significantTokens[0] +
                " " +
                significantTokens[^1]);
        }

        if (significantTokens.Count >= 3)
        {
            terms.Add(
                significantTokens[0] +
                " " +
                significantTokens[1]);
        }

        return terms
            .Where(term =>
                !string.IsNullOrWhiteSpace(term))
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<CoauthorTokenPair>
        BuildCoauthorTokenPairs(string displayName)
    {
        var tokens =
            TokenizePersonName(displayName)
                .Where(token =>
                    token.Length > 1)
                .Distinct(
                    StringComparer.Ordinal)
                .ToList();

        var pairs =
            new List<CoauthorTokenPair>();

        if (tokens.Count >= 2)
        {
            pairs.Add(
                new CoauthorTokenPair(
                    tokens[0],
                    tokens[^1]));
        }

        if (tokens.Count >= 3)
        {
            pairs.Add(
                new CoauthorTokenPair(
                    tokens[1],
                    tokens[^1]));

            pairs.Add(
                new CoauthorTokenPair(
                    tokens[0],
                    tokens[1]));
        }

        return pairs
            .Where(pair =>
                pair.Token1 != pair.Token2)
            .Distinct()
            .ToList();
    }

    private static int CalculateRosterNameScore(
        string? candidateName,
        string rosterName)
    {
        var candidateTokens =
            TokenizePersonName(candidateName)
                .Where(token =>
                    token.Length > 1)
                .ToList();

        var rosterTokens =
            TokenizePersonName(rosterName)
                .Where(token =>
                    token.Length > 1)
                .ToList();

        if (candidateTokens.Count == 0 ||
            rosterTokens.Count < 2)
        {
            return 0;
        }

        if (candidateTokens.SequenceEqual(
                rosterTokens))
        {
            return 100;
        }

        var firstMatches =
            candidateTokens[0] ==
            rosterTokens[0];

        var lastMatches =
            candidateTokens[^1] ==
            rosterTokens[^1];

        if (firstMatches &&
            lastMatches)
        {
            return 90;
        }

        var overlap =
            candidateTokens
                .Intersect(
                    rosterTokens,
                    StringComparer.Ordinal)
                .Count();

        if (lastMatches &&
            overlap >= 2)
        {
            return 85;
        }

        var candidateSubset =
            firstMatches &&
            candidateTokens.Count >= 2 &&
            candidateTokens.All(token =>
                rosterTokens.Contains(
                    token,
                    StringComparer.Ordinal));

        var rosterSubset =
            firstMatches &&
            rosterTokens.Count >= 2 &&
            rosterTokens.All(token =>
                candidateTokens.Contains(
                    token,
                    StringComparer.Ordinal));

        if (candidateSubset ||
            rosterSubset)
        {
            return 80;
        }

        return firstMatches &&
               overlap >= 2
            ? 75
            : 0;
    }

    private static bool IsUvaEmail(string? email)
    {
        return !string.IsNullOrWhiteSpace(email) &&
               email.Trim().EndsWith(
                   "@virginia.edu",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUvaInstitution(
        string? institution)
    {
        if (string.IsNullOrWhiteSpace(
                institution))
        {
            return false;
        }

        var value =
            institution.Trim();

        return value.Contains(
                   "University of Virginia",
                   StringComparison.OrdinalIgnoreCase)
               ||
               value.Contains(
                   "UVA Health",
                   StringComparison.OrdinalIgnoreCase)
               ||
               string.Equals(
                   value,
                   "UVA",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUvaSchoolOfMedicineAffiliation(
        AcademicOnlineDepartmentAffiliation affiliation)
    {
        return string.Equals(
                   affiliation.Name,
                   "School of Medicine",
                   StringComparison.OrdinalIgnoreCase)
               ||
               string.Equals(
                   affiliation.ParentName,
                   "School of Medicine",
                   StringComparison.OrdinalIgnoreCase)
               ||
               (!string.IsNullOrWhiteSpace(
                    affiliation.ParentSlug) &&
                affiliation.ParentSlug.Contains(
                    "school-of-medicine",
                    StringComparison.OrdinalIgnoreCase))
               ||
               (!string.IsNullOrWhiteSpace(
                    affiliation.Slug) &&
                affiliation.Slug.Contains(
                    "school-of-medicine",
                    StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNeurosurgeryAffiliation(
        AcademicOnlineDepartmentAffiliation affiliation)
    {
        return ContainsNeurosurgeryTerm(
                   affiliation.Name)
               ||
               ContainsNeurosurgeryTerm(
                   affiliation.ParentName)
               ||
               ContainsNeurosurgeryTerm(
                   affiliation.Slug)
               ||
               ContainsNeurosurgeryTerm(
                   affiliation.ParentSlug);
    }

    private static bool ContainsNeurosurgeryTerm(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains(
                   "neurosurg",
                   StringComparison.OrdinalIgnoreCase)
               ||
               value.Contains(
                   "neurological-surg",
                   StringComparison.OrdinalIgnoreCase)
               ||
               value.Contains(
                   "neurological surg",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAffiliationEvidence(
        IReadOnlyList<AcademicOnlineDepartmentAffiliation>
            affiliations)
    {
        if (affiliations.Count == 0)
        {
            return "(none)";
        }

        return string.Join(
            " > ",
            affiliations
                .Take(6)
                .Select(item =>
                    $"{item.Name}" +
                    (string.IsNullOrWhiteSpace(
                         item.ParentName)
                        ? string.Empty
                        : $"[{item.ParentName}]")));
    }

    private static string FormatRosterEvaluation(
        RosterCandidateEvaluation evaluation)
    {
        return
            $"AuthorId={evaluation.Candidate.AuthorId};" +
            $"Name={evaluation.ProfileName ?? evaluation.Candidate.Name ?? "(null)"};" +
            $"Email={evaluation.ProfileEmail ?? evaluation.Candidate.Email ?? "(null)"};" +
            $"Institution={evaluation.ProfileInstitution ?? "(null)"};" +
            $"NameScore={evaluation.NameScore};" +
            $"ExactExpectedEmail={evaluation.ExactExpectedEmail};" +
            $"UvaEvidence={evaluation.UvaEvidence};" +
            $"NeurosurgeryEvidence={evaluation.NeurosurgeryEvidence};" +
            $"Score={evaluation.Score};" +
            $"Affiliations={evaluation.AffiliationEvidence}";
    }

    private static List<string> TokenizePersonName(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new List<string>();
        }

        var normalized =
            new string(
                value
                    .ToLowerInvariant()
                    .Select(character =>
                        char.IsLetterOrDigit(character)
                            ? character
                            : ' ')
                    .ToArray());

        return normalized
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private async Task<OpenAlexFallbackProcessResult>
        ProcessOpenAlexRosterFallbackAsync(
            long runId,
            long departmentId,
            RosterPublicationSource rosterSource,
            AcademicOnlineDepartmentSyncRequest request,
            CancellationToken cancellationToken)
    {
        if (request.VerifiedOnly)
        {
            throw new InvalidOperationException(
                "OpenAlex fallback publications cannot satisfy Academic Online VerifiedOnly policy.");
        }

        var filter =
            BuildOpenAlexAuthorWorksFilter(
                rosterSource.SourceAuthorId,
                request.FromYear,
                request.ToYear);

        var cursor = "*";
        var pagesFetched = 0;
        var publicationsProcessed = 0;
        var hitMaxPages = false;
        var maxPages =
            Math.Clamp(
                _options.MaxPublicationPages,
                1,
                1000);

        while (!cancellationToken.IsCancellationRequested)
        {
            using var page =
                await _openAlexClient.GetWorksPageAsync(
                    filter,
                    cursor,
                    cancellationToken);

            pagesFetched++;

            foreach (var work in
                     page.ResultsElement
                         .EnumerateArray())
            {
                var publicationYear =
                    GetInt(
                        work,
                        "publication_year");

                if (request.FromYear.HasValue &&
                    publicationYear.HasValue &&
                    publicationYear.Value <
                    request.FromYear.Value)
                {
                    continue;
                }

                if (request.ToYear.HasValue &&
                    publicationYear.HasValue &&
                    publicationYear.Value >
                    request.ToYear.Value)
                {
                    continue;
                }

                var openAlexId =
                    GetString(
                        work,
                        "id");

                var doi =
                    GetString(
                        work,
                        "doi");

                var title =
                    GetString(
                        work,
                        "title")
                    ?? GetString(
                        work,
                        "display_name");

                var venue =
                    GetNestedString(
                        work,
                        "primary_location",
                        "source",
                        "display_name");

                var link =
                    GetNestedString(
                        work,
                        "primary_location",
                        "landing_page_url")
                    ?? doi
                    ?? openAlexId;

                var publicationKey =
                    await _repository.UpsertPublicationAsync(
                        runId: runId,
                        academicOnlinePublicationId: null,
                        openAlexId: openAlexId,
                        doi: doi,
                        title: title,
                        venue: venue,
                        yearText:
                            publicationYear?.ToString(),
                        publicationYear:
                            publicationYear,
                        citationCount:
                            GetInt(
                                work,
                                "cited_by_count"),
                        verified: null,
                        link: link,
                        coauthorsJson:
                            BuildOpenAlexCoauthorsJson(
                                work),
                        keywordsJson:
                            GetRawJsonProperty(
                                work,
                                "keywords"),
                        rawJson:
                            work.GetRawText(),
                        cancellationToken:
                            cancellationToken);

                await _repository
                    .TouchDepartmentPublicationAsync(
                        runId,
                        departmentId,
                        publicationKey,
                        cancellationToken);

                await _repository
                    .TouchDepartmentRosterPublicationAsync(
                        runId,
                        departmentId,
                        rosterSource.RosterMemberId,
                        publicationKey,
                        "OpenAlex",
                        rosterSource.SourceAuthorId,
                        cancellationToken);

                publicationsProcessed++;
            }

            var nextCursor =
                page.NextCursor;

            if (pagesFetched >= maxPages)
            {
                hitMaxPages =
                    !string.IsNullOrWhiteSpace(
                        nextCursor) &&
                    !string.Equals(
                        nextCursor,
                        cursor,
                        StringComparison.Ordinal);
                break;
            }

            if (string.IsNullOrWhiteSpace(
                    nextCursor) ||
                string.Equals(
                    nextCursor,
                    cursor,
                    StringComparison.Ordinal))
            {
                break;
            }

            cursor = nextCursor;
        }

        return new OpenAlexFallbackProcessResult(
            PublicationsProcessed:
                publicationsProcessed,
            PagesFetched:
                pagesFetched,
            HitMaxPages:
                hitMaxPages);
    }

    private static string
        BuildOpenAlexAuthorWorksFilter(
            string openAlexAuthorId,
            int? fromYear,
            int? toYear)
    {
        var normalizedAuthorId =
            NormalizeOpenAlexAuthorId(
                openAlexAuthorId)
            ?? throw new InvalidOperationException(
                $"Invalid OpenAlex author ID '{openAlexAuthorId}'.");

        var filters =
            new List<string>
            {
                $"authorships.author.id:{normalizedAuthorId}"
            };

        if (fromYear.HasValue)
        {
            filters.Add(
                $"from_publication_date:{fromYear.Value:D4}-01-01");
        }

        if (toYear.HasValue)
        {
            filters.Add(
                $"to_publication_date:{toYear.Value:D4}-12-31");
        }

        return string.Join(
            ",",
            filters);
    }

    private static string?
        GetNestedString(
            JsonElement element,
            params string[] path)
    {
        var current = element;

        foreach (var segment in path)
        {
            if (current.ValueKind !=
                    JsonValueKind.Object ||
                !current.TryGetProperty(
                    segment,
                    out current))
            {
                return null;
            }
        }

        if (current.ValueKind ==
            JsonValueKind.Null ||
            current.ValueKind ==
            JsonValueKind.Undefined)
        {
            return null;
        }

        return current.ValueKind ==
            JsonValueKind.String
                ? current.GetString()
                : current.ToString();
    }

    private static string?
        GetRawJsonProperty(
            JsonElement element,
            string propertyName)
    {
        if (element.ValueKind !=
                JsonValueKind.Object ||
            !element.TryGetProperty(
                propertyName,
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null ||
            value.ValueKind ==
                JsonValueKind.Undefined)
        {
            return null;
        }

        return value.GetRawText();
    }

    private static string?
        BuildOpenAlexCoauthorsJson(
            JsonElement work)
    {
        if (work.ValueKind !=
                JsonValueKind.Object ||
            !work.TryGetProperty(
                "authorships",
                out var authorships) ||
            authorships.ValueKind !=
                JsonValueKind.Array)
        {
            return null;
        }

        var rows =
            new List<Dictionary<string, string?>>();

        foreach (var authorship in
                 authorships.EnumerateArray())
        {
            if (authorship.ValueKind !=
                    JsonValueKind.Object ||
                !authorship.TryGetProperty(
                    "author",
                    out var author) ||
                author.ValueKind !=
                    JsonValueKind.Object)
            {
                continue;
            }

            var name =
                GetString(
                    author,
                    "display_name");

            var authorId =
                NormalizeOpenAlexAuthorId(
                    GetString(
                        author,
                        "id"));

            if (string.IsNullOrWhiteSpace(
                    name) &&
                string.IsNullOrWhiteSpace(
                    authorId))
            {
                continue;
            }

            rows.Add(
                new Dictionary<string, string?>
                {
                    ["name"] = name,
                    ["openalex_id"] =
                        authorId
                });
        }

        return rows.Count == 0
            ? null
            : JsonSerializer.Serialize(rows);
    }

    private static void ValidateYearRange(
        int? fromYear,
        int? toYear)
    {
        if (fromYear.HasValue &&
            toYear.HasValue &&
            fromYear > toYear)
        {
            throw new InvalidOperationException(
                "Academic Online department sync FromYear cannot be greater than ToYear.");
        }

        foreach (var year in new[] { fromYear, toYear })
        {
            if (year.HasValue &&
                (year < 1800 ||
                 year > DateTime.UtcNow.Year + 1))
            {
                throw new InvalidOperationException(
                    "Academic Online department sync contains a publication year outside the allowed range.");
            }
        }
    }

    private static string? GetString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static int? GetInt(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(
            value.ToString(),
            out var parsed)
            ? parsed
            : null;
    }

    private static bool? GetBool(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return bool.TryParse(
            value.ToString(),
            out var parsed)
            ? parsed
            : null;
    }
}

internal sealed class ScholarSelection
{
    public string SourceMode { get; init; } = "DirectDepartment";
    public IReadOnlyList<AcademicOnlineScholarSearchCandidate> Scholars { get; init; } =
        Array.Empty<AcademicOnlineScholarSearchCandidate>();
    public IReadOnlyList<RosterPublicationSource> RosterPublicationSources { get; init; } =
        Array.Empty<RosterPublicationSource>();
    public int ScholarsDiscovered { get; init; }
    public int ScholarPagesFetched { get; init; }
    public bool ScholarPaginationSafeguardHit { get; init; }
    public bool SourceMembershipComplete { get; init; }
    public string SourceDetail { get; init; } = string.Empty;
}

internal sealed record RosterResolution(
    AcademicOnlineScholarSearchCandidate? Candidate,
    int SearchRequests,
    string? ResolutionMethod,
    string? Diagnostic);

internal sealed record CoauthorTokenPair(
    string Token1,
    string Token2);

internal sealed record OpenAlexSeedAuthorEvidence(
    long IdentitySeedId,
    string Doi,
    string OpenAlexWorkId,
    string OpenAlexAuthorId,
    string? AuthorName,
    int NameScore,
    string? SourceReference);

internal sealed record OpenAlexFallbackPreview(
    string? OpenAlexAuthorId,
    string Status,
    string Evidence);

internal sealed record OpenAlexFallbackCandidate(
    string OpenAlexAuthorId,
    string? DisplayName,
    string? Orcid,
    int NameScore,
    bool HasUvaAffiliation,
    string UvaEvidence,
    int? WorksCount,
    int? CitedByCount);

internal sealed record OpenAlexUvaEvidence(
    bool IsUva,
    string Evidence);

internal sealed record RosterPublicationSource(
    long RosterMemberId,
    string DisplayName,
    string SourceProvider,
    string SourceAuthorId);

internal sealed record OpenAlexFallbackProcessResult(
    int PublicationsProcessed,
    int PagesFetched,
    bool HitMaxPages);

internal sealed record RosterCandidateEvaluation(
    AcademicOnlineScholarSearchCandidate Candidate,
    string? ProfileName,
    string? ProfileEmail,
    string? ProfileInstitution,
    int NameScore,
    bool ExactExpectedEmail,
    bool UvaEvidence,
    bool NeurosurgeryEvidence,
    int Score,
    string AffiliationEvidence);

public sealed class AcademicOnlineDepartmentSyncRequest
{
    public long DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public string SourceMode { get; init; } = "DirectDepartment";
    public int? FromYear { get; init; }
    public int? ToYear { get; init; }
    public bool VerifiedOnly { get; init; }
    public int MaxScholarsToProcess { get; init; } = 250;
    public string StartedBy { get; init; } =
        "OpenAlex.V3E.PATCH020";
    public string ApplicationVersion { get; init; } =
        "3E-PATCH020-DepartmentCatalogScheduledRefresh";
}

public sealed class AcademicOnlineDepartmentSyncOutcome
{
    public long? RunId { get; init; }
    public bool Succeeded { get; init; }
    public bool IsCacheComplete { get; init; }
    public int ScholarsDiscovered { get; init; }
    public int ScholarsProcessed { get; init; }
    public int PublicationsProcessed { get; init; }
    public string? ErrorMessage { get; init; }

    public static AcademicOnlineDepartmentSyncOutcome Failed(
        long? runId,
        string errorMessage)
    {
        return new AcademicOnlineDepartmentSyncOutcome
        {
            RunId = runId,
            Succeeded = false,
            IsCacheComplete = false,
            ErrorMessage = errorMessage
        };
    }
}
