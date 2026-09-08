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
    private readonly AcademicOnlineReportingStoreRepository _repository;
    private readonly IOperationalLogger _logger;
    private readonly AcademicOnlineDepartmentSyncOptions _options;

    public AcademicOnlineDepartmentSyncJob(
        AcademicOnlineApiClient client,
        AcademicOnlineReportingStoreRepository repository,
        IOperationalLogger logger,
        IOptions<AcademicOnlineDepartmentSyncOptions> options)
    {
        _client = client;
        _repository = repository;
        _logger = logger;
        _options = options.Value;
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

            var scholars = selection.Scholars
                .Take(maxScholars)
                .ToList();

            foreach (var candidate in scholars)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var generalDocument =
                    await _client.GetAuthorGeneralAsync(
                        candidate.AuthorId,
                        cancellationToken);

                var general = generalDocument.RootElement;

                var displayName =
                    GetString(general, "name")
                    ?? candidate.Name
                    ?? candidate.AuthorId;

                var email =
                    GetString(general, "email")
                    ?? candidate.Email;

                var position =
                    GetString(general, "position")
                    ?? candidate.Position;

                var institution =
                    GetString(general, "institution")
                    ?? candidate.Institution;

                var orcidUrl =
                    GetString(general, "orcid_url")
                    ?? candidate.OrcidUrl;

                await _repository.UpsertScholarAsync(
                    runId,
                    candidate.AuthorId,
                    displayName,
                    email,
                    position,
                    institution,
                    orcidUrl,
                    GetInt(general, "publication_count"),
                    GetBool(general, "has_publications"),
                    GetBool(general, "has_verified_publications"),
                    general.GetRawText(),
                    cancellationToken);

                await _repository.TouchScholarDepartmentAsync(
                    runId,
                    candidate.AuthorId,
                    departmentId,
                    cancellationToken);

                var publications =
                    await _client.GetAllAuthorPublicationsAsync(
                        candidate.AuthorId,
                        request.VerifiedOnly,
                        rowsPerPage: Math.Clamp(
                            _options.PublicationRowsPerPage,
                            1,
                            100),
                        maxPages: Math.Clamp(
                            _options.MaxPublicationPages,
                            1,
                            1000),
                        fromYear: request.FromYear,
                        toYear: request.ToYear,
                        cancellationToken: cancellationToken);

                publicationPagesFetched += publications.PagesFetched;

                if (publications.HitMaxPages)
                {
                    publicationPaginationSafeguardHitCount++;
                }

                foreach (var publication in publications.Publications)
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

                    await _repository.TouchScholarPublicationAsync(
                        runId,
                        candidate.AuthorId,
                        publicationKey,
                        cancellationToken);

                    await _repository.TouchDepartmentPublicationAsync(
                        runId,
                        departmentId,
                        publicationKey,
                        cancellationToken);

                    await _repository.TouchDepartmentScholarPublicationAsync(
                        runId,
                        departmentId,
                        candidate.AuthorId,
                        publicationKey,
                        cancellationToken);

                    publicationRowsProcessed++;
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

        var searchRequests = 0;
        var unresolvedMembers = new List<string>();

        foreach (var member in roster)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AcademicOnlineScholarSearchCandidate? resolved = null;
            string? resolutionDiagnostic = null;

            if (!string.IsNullOrWhiteSpace(member.AcademicOnlineAuthorId))
            {
                resolved =
                    new AcademicOnlineScholarSearchCandidate
                    {
                        AuthorId = member.AcademicOnlineAuthorId.Trim(),
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
                resolutionDiagnostic = resolution.Diagnostic;
                searchRequests += resolution.SearchRequests;
            }

            if (resolved is null)
            {
                unresolvedMembers.Add(member.DisplayName);

                await _repository.RecordRosterMemberResolutionAsync(
                    member.RosterMemberId,
                    academicOnlineAuthorId: null,
                    status: "Unresolved",
                    errorMessage:
                        resolutionDiagnostic
                        ?? "No unique Academic Online scholar identity matched the official roster member.",
                    cancellationToken: CancellationToken.None);

                continue;
            }

            scholarsById[resolved.AuthorId] = resolved;

            await _repository.RecordRosterMemberResolutionAsync(
                member.RosterMemberId,
                resolved.AuthorId,
                status: "Resolved",
                errorMessage: null,
                cancellationToken: CancellationToken.None);
        }

        return new ScholarSelection
        {
            SourceMode = "FacultyRoster",
            Scholars =
                scholarsById.Values
                    .OrderBy(
                        item => item.Name ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            ScholarsDiscovered = roster.Count,
            ScholarPagesFetched = searchRequests,
            ScholarPaginationSafeguardHit = false,
            SourceMembershipComplete =
                unresolvedMembers.Count == 0 &&
                scholarsById.Count == roster.Count,
            SourceDetail =
                $"Roster resolved={scholarsById.Count}/{roster.Count}; " +
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

        if (candidatesById.Count == 0)
        {
            return new RosterResolution(
                Candidate: null,
                SearchRequests: searchRequests,
                Diagnostic:
                    $"No Academic Online candidates returned. {string.Join(" | ", searchNotes)}");
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
                    Diagnostic: "Resolved by exact UVA email.");
            }
        }

        var candidatesToEvaluate =
            candidatesById.Values
                .Select(candidate =>
                    new
                    {
                        Candidate = candidate,
                        NameScore = CalculateRosterNameScore(
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
                .OrderByDescending(item => item.NameScore)
                .ThenByDescending(item =>
                    item.Candidate.RankingScore ?? 0m)
                .Take(10)
                .ToList();

        if (candidatesToEvaluate.Count == 0)
        {
            return new RosterResolution(
                Candidate: null,
                SearchRequests: searchRequests,
                Diagnostic:
                    $"Candidates returned, but none passed name compatibility. " +
                    $"Roster={member.DisplayName}; " +
                    $"Candidates={string.Join(",", candidatesById.Values.Select(item => item.Name ?? item.AuthorId))}.");
        }

        var evaluations = new List<RosterCandidateEvaluation>();

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

                var root = profile.RootElement;
                var profileName =
                    GetString(root, "name")
                    ?? candidate.Name;
                var profileEmail =
                    GetString(root, "email")
                    ?? candidate.Email;
                var profileInstitution =
                    GetString(root, "institution")
                    ?? candidate.Institution;

                var nameScore =
                    Math.Max(
                        item.NameScore,
                        CalculateRosterNameScore(
                            profileName,
                            member.DisplayName));

                var exactEmail =
                    !string.IsNullOrWhiteSpace(expectedEmail) &&
                    string.Equals(
                        profileEmail?.Trim(),
                        expectedEmail,
                        StringComparison.OrdinalIgnoreCase);

                var affiliations =
                    AcademicOnlineDepartmentParser
                        .GetDepartmentAffiliations(root);

                var uvaEvidence =
                    exactEmail ||
                    IsUvaEmail(profileEmail) ||
                    IsUvaInstitution(profileInstitution) ||
                    affiliations.Any(IsUvaSchoolOfMedicineAffiliation);

                var neurosurgeryEvidence =
                    affiliations.Any(IsNeurosurgeryAffiliation);

                var score =
                    nameScore +
                    (exactEmail ? 300 : 0) +
                    (uvaEvidence ? 100 : 0) +
                    (neurosurgeryEvidence ? 25 : 0);

                evaluations.Add(
                    new RosterCandidateEvaluation(
                        candidate,
                        profileName,
                        profileEmail,
                        profileInstitution,
                        nameScore,
                        exactEmail,
                        uvaEvidence,
                        neurosurgeryEvidence,
                        score,
                        BuildAffiliationEvidence(affiliations)));
            }
            catch (HttpRequestException ex)
            {
                searchNotes.Add(
                    $"ProfileError={candidate.AuthorId}:{ex.Message}");
            }
            catch (TaskCanceledException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                searchNotes.Add(
                    $"ProfileTimeout={candidate.AuthorId}:{ex.Message}");
            }
        }

        var defensible =
            evaluations
                .Where(item =>
                    item.NameScore >= 70 &&
                    item.UvaEvidence)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item =>
                    item.Candidate.RankingScore ?? 0m)
                .ToList();

        if (defensible.Count == 1)
        {
            return new RosterResolution(
                defensible[0].Candidate,
                searchRequests,
                Diagnostic:
                    $"Resolved by profile-validated UVA identity. {FormatRosterEvaluation(defensible[0])}");
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
                    Diagnostic:
                        $"Resolved by profile exact-email tie-breaker. {FormatRosterEvaluation(top)}");
            }

            if (top.Score - second.Score >= 25)
            {
                return new RosterResolution(
                    top.Candidate,
                    searchRequests,
                    Diagnostic:
                        $"Resolved by unique profile evidence score. {FormatRosterEvaluation(top)}");
            }
        }

        var evaluationText =
            evaluations.Count == 0
                ? "(no profiles evaluated)"
                : string.Join(
                    " || ",
                    evaluations.Select(FormatRosterEvaluation));

        return new RosterResolution(
            Candidate: null,
            SearchRequests: searchRequests,
            Diagnostic:
                $"No unique profile-validated UVA identity. " +
                $"{string.Join(" | ", searchNotes)} | Evaluations={evaluationText}");
    }

    private static IReadOnlyList<string> BuildRosterSearchTerms(
        AcademicOnlineDepartmentRosterMember member)
    {
        var terms =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(member.ExpectedEmail))
        {
            var expectedEmail =
                member.ExpectedEmail.Trim();

            terms.Add(expectedEmail);

            var atIndex =
                expectedEmail.IndexOf('@');

            if (atIndex > 0)
            {
                terms.Add(expectedEmail[..atIndex]);
            }
        }

        terms.Add(member.DisplayName.Trim());

        var significantTokens =
            TokenizePersonName(member.DisplayName)
                .Where(token => token.Length > 1)
                .ToList();

        if (significantTokens.Count >= 2)
        {
            terms.Add(
                string.Join(" ", significantTokens));

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

    private static int CalculateRosterNameScore(
        string? candidateName,
        string rosterName)
    {
        var candidateTokens =
            TokenizePersonName(candidateName)
                .Where(token => token.Length > 1)
                .ToList();

        var rosterTokens =
            TokenizePersonName(rosterName)
                .Where(token => token.Length > 1)
                .ToList();

        if (candidateTokens.Count == 0 ||
            rosterTokens.Count < 2)
        {
            return 0;
        }

        if (candidateTokens.SequenceEqual(rosterTokens))
        {
            return 100;
        }

        var firstMatches =
            candidateTokens[0] == rosterTokens[0];

        var lastMatches =
            candidateTokens[^1] == rosterTokens[^1];

        if (firstMatches && lastMatches)
        {
            return 90;
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

        if (candidateSubset || rosterSubset)
        {
            return 85;
        }

        var overlap =
            candidateTokens
                .Intersect(
                    rosterTokens,
                    StringComparer.Ordinal)
                .Count();

        return firstMatches && overlap >= 2
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

    private static bool IsUvaInstitution(string? institution)
    {
        if (string.IsNullOrWhiteSpace(institution))
        {
            return false;
        }

        var value = institution.Trim();

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
               (!string.IsNullOrWhiteSpace(affiliation.ParentSlug) &&
                affiliation.ParentSlug.Contains(
                    "school-of-medicine",
                    StringComparison.OrdinalIgnoreCase))
               ||
               (!string.IsNullOrWhiteSpace(affiliation.Slug) &&
                affiliation.Slug.Contains(
                    "school-of-medicine",
                    StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNeurosurgeryAffiliation(
        AcademicOnlineDepartmentAffiliation affiliation)
    {
        return ContainsNeurosurgeryTerm(affiliation.Name) ||
               ContainsNeurosurgeryTerm(affiliation.ParentName) ||
               ContainsNeurosurgeryTerm(affiliation.Slug) ||
               ContainsNeurosurgeryTerm(affiliation.ParentSlug);
    }

    private static bool ContainsNeurosurgeryTerm(string? value)
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
        IReadOnlyList<AcademicOnlineDepartmentAffiliation> affiliations)
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
                    (string.IsNullOrWhiteSpace(item.ParentName)
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

    private static List<string> TokenizePersonName(string? value)
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
    public int ScholarsDiscovered { get; init; }
    public int ScholarPagesFetched { get; init; }
    public bool ScholarPaginationSafeguardHit { get; init; }
    public bool SourceMembershipComplete { get; init; }
    public string SourceDetail { get; init; } = string.Empty;
}

internal sealed record RosterResolution(
    AcademicOnlineScholarSearchCandidate? Candidate,
    int SearchRequests,
    string? Diagnostic);

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
