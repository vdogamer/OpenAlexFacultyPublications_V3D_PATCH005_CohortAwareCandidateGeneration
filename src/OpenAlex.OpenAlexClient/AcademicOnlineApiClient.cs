using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OpenAlex.OpenAlexClient;

public sealed class AcademicOnlineApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AcademicOnlineOptions _options;

    public AcademicOnlineApiClient(HttpClient httpClient, IOptions<AcademicOnlineOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }

        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(10, _options.TimeoutSeconds));
    }

    public Task<JsonDocument> SearchScholarsAsync(
        string facultyDisplayName,
        string? departmentName,
        int page,
        int rowsPerPage,
        CancellationToken cancellationToken = default)
    {
        var query = string.IsNullOrWhiteSpace(facultyDisplayName)
            ? Array.Empty<string>()
            : new[] { facultyDisplayName.Trim() };

        var departments = string.IsNullOrWhiteSpace(departmentName)
            ? Array.Empty<string>()
            : new[] { departmentName.Trim() };

        var body = new
        {
            full_text_search_query = facultyDisplayName?.Trim() ?? string.Empty,
            query,
            departments,
            page,
            rowsPerPage,
            advanced_search_input = new { }
        };

        return PostJsonDocumentAsync("scholars/filter_scholars_with_fts", body, cancellationToken);
    }

    public async Task<IReadOnlyList<AcademicOnlineScholarSearchCandidate>> SearchScholarCandidatesAsync(
        string searchText,
        string? departmentName = null,
        int rowsPerPage = 25,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return Array.Empty<AcademicOnlineScholarSearchCandidate>();
        }

        using var document = await SearchScholarsAsync(
            searchText.Trim(),
            departmentName,
            page: 0,
            rowsPerPage: Math.Clamp(rowsPerPage, 1, 100),
            cancellationToken);

        if (!document.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AcademicOnlineScholarSearchCandidate>();
        }

        var candidates = new List<AcademicOnlineScholarSearchCandidate>();

        foreach (var item in results.EnumerateArray())
        {
            var authorId = GetString(item, "id");
            if (string.IsNullOrWhiteSpace(authorId))
            {
                continue;
            }

            candidates.Add(new AcademicOnlineScholarSearchCandidate
            {
                AuthorId = authorId.Trim(),
                Name = GetString(item, "name"),
                Email = GetString(item, "email"),
                Position = GetString(item, "position"),
                Department =
                    AcademicOnlineDepartmentParser.GetPreferredDepartmentName(item),
                Institution =
                    GetString(item, "institution")
                    ?? GetString(item, "institution_name"),
                OrcidUrl =
                    GetString(item, "orcid_url")
                    ?? GetString(item, "orcid"),
                RankingScore = GetDecimal(item, "ranking_score")
            });
        }

        return candidates
            .OrderByDescending(candidate => candidate.RankingScore ?? decimal.MinValue)
            .ThenBy(candidate => candidate.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AcademicOnlineDepartmentDiscoveryCollection>
        DiscoverDepartmentsAsync(
            string parentName,
            int rowsPerPage = 100,
            int maxPages = 100,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentName))
        {
            throw new ArgumentException(
                "Academic Online parent organization name is required.",
                nameof(parentName));
        }

        var normalizedParentName = parentName.Trim();
        var pageSize = Math.Clamp(rowsPerPage, 1, 100);
        var pageLimit = Math.Clamp(maxPages, 1, 1000);

        var seenAuthorIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var structuredAffiliations =
            new Dictionary<string, DepartmentDiscoveryAccumulator>(
                StringComparer.OrdinalIgnoreCase);

        var candidateDepartments =
            new Dictionary<string, CandidateDepartmentAccumulator>(
                StringComparer.OrdinalIgnoreCase);

        var pagesFetched = 0;
        var affiliationsObserved = 0;
        var profilesFetched = 0;
        var profileValidationFailures = 0;
        var hitMaxPages = false;

        for (var page = 0; page < pageLimit; page++)
        {
            using var document = await SearchScholarsAsync(
                facultyDisplayName: string.Empty,
                departmentName: null,
                page: page,
                rowsPerPage: pageSize,
                cancellationToken: cancellationToken);

            pagesFetched++;

            if (!document.RootElement.TryGetProperty(
                    "results",
                    out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var pageRowCount = 0;
            var uniqueBefore = seenAuthorIds.Count;

            foreach (var item in results.EnumerateArray())
            {
                pageRowCount++;

                var authorId = GetString(item, "id");
                if (string.IsNullOrWhiteSpace(authorId))
                {
                    continue;
                }

                var normalizedAuthorId = authorId.Trim();
                if (!seenAuthorIds.Add(normalizedAuthorId))
                {
                    continue;
                }

                var affiliations =
                    AcademicOnlineDepartmentParser
                        .GetDepartmentAffiliations(item);

                affiliationsObserved += affiliations.Count;

                foreach (var affiliation in affiliations)
                {
                    var key = BuildDepartmentDiscoveryKey(
                        affiliation);

                    if (!structuredAffiliations.TryGetValue(
                            key,
                            out var accumulator))
                    {
                        accumulator =
                            new DepartmentDiscoveryAccumulator(
                                affiliation);

                        structuredAffiliations.Add(
                            key,
                            accumulator);
                    }

                    accumulator.AuthorIds.Add(
                        normalizedAuthorId);
                }

                var preferredDepartment =
                    AcademicOnlineDepartmentParser
                        .GetPreferredDepartmentName(item);

                if (!string.IsNullOrWhiteSpace(preferredDepartment) &&
                    !string.Equals(
                        preferredDepartment,
                        normalizedParentName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    var candidateName =
                        preferredDepartment.Trim();

                    if (!candidateDepartments.TryGetValue(
                            candidateName,
                            out var candidate))
                    {
                        candidate =
                            new CandidateDepartmentAccumulator(
                                candidateName,
                                normalizedAuthorId);

                        candidateDepartments.Add(
                            candidateName,
                            candidate);
                    }

                    candidate.AuthorIds.Add(
                        normalizedAuthorId);
                }
            }

            var newUniqueAuthors =
                seenAuthorIds.Count - uniqueBefore;

            if (pageRowCount == 0 ||
                newUniqueAuthors == 0)
            {
                break;
            }

            if (page == pageLimit - 1)
            {
                hitMaxPages = true;
            }
        }

        var allAffiliations =
            structuredAffiliations.Values
                .Select(item => item.Affiliation)
                .ToList();

        var parentIds =
            allAffiliations
                .Where(item =>
                    string.Equals(
                        item.Name,
                        normalizedParentName,
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Id))
                .Select(item => item.Id!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var parentSlugs =
            allAffiliations
                .Where(item =>
                    string.Equals(
                        item.Name,
                        normalizedParentName,
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Slug))
                .Select(item => item.Slug!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var validated =
            new Dictionary<string, DepartmentDiscoveryAccumulator>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var item in structuredAffiliations.Values)
        {
            if (!IsDirectChildOfParent(
                    item.Affiliation,
                    normalizedParentName,
                    parentIds,
                    parentSlugs))
            {
                continue;
            }

            MergeValidatedDepartment(
                validated,
                item.Affiliation,
                item.AuthorIds);
        }

        foreach (var candidate in candidateDepartments.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (validated.Values.Any(item =>
                    string.Equals(
                        item.Affiliation.Name,
                        candidate.DepartmentName,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                using var generalDocument =
                    await GetAuthorGeneralAsync(
                        candidate.RepresentativeAuthorId,
                        cancellationToken);

                profilesFetched++;

                var profileAffiliations =
                    AcademicOnlineDepartmentParser
                        .GetDepartmentAffiliations(
                            generalDocument.RootElement);

                affiliationsObserved +=
                    profileAffiliations.Count;

                var profileParentIds =
                    profileAffiliations
                        .Where(item =>
                            string.Equals(
                                item.Name,
                                normalizedParentName,
                                StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(item.Id))
                        .Select(item => item.Id!)
                        .ToHashSet(
                            StringComparer.OrdinalIgnoreCase);

                var profileParentSlugs =
                    profileAffiliations
                        .Where(item =>
                            string.Equals(
                                item.Name,
                                normalizedParentName,
                                StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(item.Slug))
                        .Select(item => item.Slug!)
                        .ToHashSet(
                            StringComparer.OrdinalIgnoreCase);

                foreach (var affiliation in profileAffiliations)
                {
                    if (!IsDirectChildOfParent(
                            affiliation,
                            normalizedParentName,
                            profileParentIds,
                            profileParentSlugs))
                    {
                        continue;
                    }

                    var evidenceAuthorIds =
                        string.Equals(
                            affiliation.Name,
                            candidate.DepartmentName,
                            StringComparison.OrdinalIgnoreCase)
                            ? candidate.AuthorIds
                            : new HashSet<string>(
                                new[]
                                {
                                    candidate.RepresentativeAuthorId
                                },
                                StringComparer.OrdinalIgnoreCase);

                    MergeValidatedDepartment(
                        validated,
                        affiliation,
                        evidenceAuthorIds);
                }
            }
            catch (HttpRequestException)
            {
                profileValidationFailures++;
            }
            catch (TaskCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                profileValidationFailures++;
            }
        }

        var departments =
            validated.Values
                .Select(item =>
                    new AcademicOnlineDepartmentDiscoveryItem
                    {
                        AcademicOnlineDepartmentId =
                            item.Affiliation.Id,
                        DepartmentName =
                            item.Affiliation.Name,
                        Slug =
                            item.Affiliation.Slug,
                        ParentAcademicOnlineDepartmentId =
                            item.Affiliation.ParentId,
                        ParentName =
                            item.Affiliation.ParentName
                            ?? normalizedParentName,
                        ParentSlug =
                            item.Affiliation.ParentSlug,
                        ScholarCount =
                            item.AuthorIds.Count
                    })
                .OrderBy(
                    item => item.DepartmentName,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        return new AcademicOnlineDepartmentDiscoveryCollection
        {
            Departments = departments,
            PagesFetched = pagesFetched,
            RowsPerPage = pageSize,
            ScholarsScanned = seenAuthorIds.Count,
            AffiliationsObserved = affiliationsObserved,
            ProfilesFetched = profilesFetched,
            ProfileValidationFailures = profileValidationFailures,
            HitMaxPages = hitMaxPages
        };
    }

    private static bool IsDirectChildOfParent(
        AcademicOnlineDepartmentAffiliation affiliation,
        string parentName,
        HashSet<string> parentIds,
        HashSet<string> parentSlugs)
    {
        if (string.Equals(
                affiliation.Name,
                parentName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return
            string.Equals(
                affiliation.ParentName,
                parentName,
                StringComparison.OrdinalIgnoreCase)
            ||
            (!string.IsNullOrWhiteSpace(
                 affiliation.ParentId) &&
             parentIds.Contains(
                 affiliation.ParentId))
            ||
            (!string.IsNullOrWhiteSpace(
                 affiliation.ParentSlug) &&
             parentSlugs.Contains(
                 affiliation.ParentSlug));
    }

    private static void MergeValidatedDepartment(
        Dictionary<string, DepartmentDiscoveryAccumulator> validated,
        AcademicOnlineDepartmentAffiliation affiliation,
        IEnumerable<string> authorIds)
    {
        var key =
            BuildDepartmentDiscoveryKey(
                affiliation);

        if (!validated.TryGetValue(
                key,
                out var accumulator))
        {
            accumulator =
                new DepartmentDiscoveryAccumulator(
                    affiliation);

            validated.Add(
                key,
                accumulator);
        }

        foreach (var authorId in authorIds)
        {
            accumulator.AuthorIds.Add(
                authorId);
        }
    }

    public async Task<AcademicOnlineDepartmentDiscoveryCollection>
        DiscoverDepartmentsFromSeedsAsync(
            string parentName,
            IReadOnlyList<AcademicOnlineDepartmentSeedCandidate> seeds,
            IReadOnlyList<AcademicOnlineDepartmentAnchorCandidate> anchors,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentName))
        {
            throw new ArgumentException(
                "Academic Online parent organization name is required.",
                nameof(parentName));
        }

        if (seeds is null || seeds.Count == 0)
        {
            return new AcademicOnlineDepartmentDiscoveryCollection();
        }

        var normalizedParentName = parentName.Trim();

        var departments =
            new List<AcademicOnlineDepartmentDiscoveryItem>();

        var unresolvedDiagnostics =
            new List<string>();

        var seenAuthorIds =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var searchRequests = 0;
        var profilesFetched = 0;
        var affiliationsObserved = 0;
        var profileValidationFailures = 0;
        var departmentsUnresolved = 0;

        foreach (var group in seeds
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.OfficialName) &&
                !string.IsNullOrWhiteSpace(item.SearchName))
            .GroupBy(
                item => item.OfficialName.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                group => group.Key,
                StringComparer.OrdinalIgnoreCase))
        {
            AcademicOnlineDepartmentDiscoveryItem?
                resolved = null;

            var groupDiagnostics =
                new List<string>();

            foreach (var seed in group
                .OrderBy(item => item.SearchPriority))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var searchDocument =
                        await SearchScholarsAsync(
                            facultyDisplayName:
                                string.Empty,
                            departmentName:
                                seed.SearchName,
                            page:
                                0,
                            rowsPerPage:
                                5,
                            cancellationToken:
                                cancellationToken);

                    searchRequests++;

                    if (!searchDocument.RootElement.TryGetProperty(
                            "results",
                            out var results) ||
                        results.ValueKind !=
                            JsonValueKind.Array ||
                        results.GetArrayLength() == 0)
                    {
                        continue;
                    }

                    var authorIds =
                        results
                            .EnumerateArray()
                            .Select(item =>
                                GetString(item, "id"))
                            .Where(id =>
                                !string.IsNullOrWhiteSpace(id))
                            .Select(id => id!.Trim())
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .ToList();

                    groupDiagnostics.Add(
                        $"Alias={seed.SearchName}; " +
                        $"ValidationParent={seed.ValidationParentName ?? normalizedParentName}; " +
                        $"Candidates={string.Join(",", authorIds)}");

                    foreach (var authorId in authorIds)
                    {
                        seenAuthorIds.Add(authorId);
                    }

                    foreach (var authorId in authorIds)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            using var profileDocument =
                                await GetAuthorGeneralAsync(
                                    authorId,
                                    cancellationToken);

                            profilesFetched++;

                            var affiliations =
                                AcademicOnlineDepartmentParser
                                    .GetDepartmentAffiliations(
                                        profileDocument.RootElement);

                            affiliationsObserved +=
                                affiliations.Count;

                            var affiliationSummary =
                                affiliations.Count == 0
                                    ? "(none)"
                                    : string.Join(
                                        " | ",
                                        affiliations.Select(item =>
                                            $"Name={item.Name};" +
                                            $"ParentName={item.ParentName ?? "(null)"};" +
                                            $"Slug={item.Slug ?? "(null)"};" +
                                            $"ParentSlug={item.ParentSlug ?? "(null)"}"));

                            groupDiagnostics.Add(
                                $"Alias={seed.SearchName}; " +
                                $"AuthorId={authorId}; " +
                                $"Affiliations={affiliationSummary}");

                            var allowedParentNames =
                                new[]
                                {
                                    normalizedParentName,
                                    seed.ValidationParentName
                                }
                                .Where(name =>
                                    !string.IsNullOrWhiteSpace(name))
                                .Select(name => name!.Trim())
                                .Distinct(
                                    StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            var parentIds =
                                affiliations
                                    .Where(item =>
                                        allowedParentNames.Any(parent =>
                                            DepartmentNamesEquivalent(
                                                item.Name,
                                                parent)) &&
                                        !string.IsNullOrWhiteSpace(
                                            item.Id))
                                    .Select(item => item.Id!)
                                    .ToHashSet(
                                        StringComparer.OrdinalIgnoreCase);

                            var parentSlugs =
                                affiliations
                                    .Where(item =>
                                        allowedParentNames.Any(parent =>
                                            DepartmentNamesEquivalent(
                                                item.Name,
                                                parent)) &&
                                        !string.IsNullOrWhiteSpace(
                                            item.Slug))
                                    .Select(item => item.Slug!)
                                    .ToHashSet(
                                        StringComparer.OrdinalIgnoreCase);

                            var directChildren =
                                affiliations
                                    .Where(item =>
                                        !allowedParentNames.Any(parent =>
                                            DepartmentNamesEquivalent(
                                                item.Name,
                                                parent)) &&
                                        (
                                            allowedParentNames.Any(parent =>
                                                DepartmentNamesEquivalent(
                                                    item.ParentName,
                                                    parent))
                                            ||
                                            (!string.IsNullOrWhiteSpace(
                                                 item.ParentId) &&
                                             parentIds.Contains(
                                                 item.ParentId))
                                            ||
                                            (!string.IsNullOrWhiteSpace(
                                                 item.ParentSlug) &&
                                             parentSlugs.Contains(
                                                 item.ParentSlug))
                                        ))
                                    .ToList();

                            var match =
                                directChildren
                                    .FirstOrDefault(item =>
                                        DepartmentNamesEquivalent(
                                            item.Name,
                                            seed.SearchName))
                                ??
                                directChildren
                                    .FirstOrDefault(item =>
                                        DepartmentNamesEquivalent(
                                            item.Name,
                                            group.Key));

                            if (match is null)
                            {
                                continue;
                            }

                            resolved =
                                new AcademicOnlineDepartmentDiscoveryItem
                                {
                                    AcademicOnlineDepartmentId =
                                        match.Id,
                                    DepartmentName =
                                        match.Name,
                                    Slug =
                                        match.Slug,
                                    ParentAcademicOnlineDepartmentId =
                                        match.ParentId,
                                    ParentName =
                                        match.ParentName
                                        ?? normalizedParentName,
                                    ParentSlug =
                                        match.ParentSlug,
                                    ScholarCount =
                                        Math.Max(
                                            1,
                                            authorIds.Count)
                                };

                            break;
                        }
                        catch (HttpRequestException ex)
                        {
                            profileValidationFailures++;

                            groupDiagnostics.Add(
                                $"Alias={seed.SearchName}; " +
                                $"AuthorId={authorId}; " +
                                $"ProfileError={ex.Message}");
                        }
                        catch (TaskCanceledException ex)
                            when (!cancellationToken.IsCancellationRequested)
                        {
                            profileValidationFailures++;

                            groupDiagnostics.Add(
                                $"Alias={seed.SearchName}; " +
                                $"AuthorId={authorId}; " +
                                $"ProfileTimeout={ex.Message}");
                        }
                    }

                    if (resolved is not null)
                    {
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                    profileValidationFailures++;
                }
                catch (TaskCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    profileValidationFailures++;
                }
            }

            if (resolved is null)
            {
                var departmentAnchors =
                    anchors
                        .Where(anchor =>
                            string.Equals(
                                anchor.OfficialName,
                                group.Key,
                                StringComparison.OrdinalIgnoreCase))
                        .OrderBy(anchor =>
                            anchor.SearchPriority)
                        .ToList();

                foreach (var anchor in departmentAnchors)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var anchorSearchText =
                            !string.IsNullOrWhiteSpace(
                                anchor.ExpectedEmail)
                                ? anchor.ExpectedEmail!
                                : anchor.ScholarSearchText;

                        var anchorCandidates =
                            await SearchScholarCandidatesAsync(
                                anchorSearchText,
                                departmentName:
                                    null,
                                rowsPerPage:
                                    10,
                                cancellationToken:
                                    cancellationToken);

                        searchRequests++;

                        var identityCandidates =
                            anchorCandidates
                                .Where(candidate =>
                                    !string.IsNullOrWhiteSpace(
                                        anchor.ExpectedEmail)
                                        ? string.Equals(
                                            candidate.Email?.Trim(),
                                            anchor.ExpectedEmail.Trim(),
                                            StringComparison.OrdinalIgnoreCase)
                                        : PersonNameMatchesAnchor(
                                            candidate.Name,
                                            anchor.ScholarSearchText))
                                .Take(5)
                                .ToList();

                        groupDiagnostics.Add(
                            $"Anchor={anchor.ScholarSearchText}; " +
                            $"ExpectedEmail={anchor.ExpectedEmail ?? "(none)"}; " +
                            $"Candidates={string.Join(",", identityCandidates.Select(item => item.AuthorId))}");

                        foreach (var candidate in
                            identityCandidates)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            seenAuthorIds.Add(
                                candidate.AuthorId);

                            try
                            {
                                using var profileDocument =
                                    await GetAuthorGeneralAsync(
                                        candidate.AuthorId,
                                        cancellationToken);

                                profilesFetched++;

                                var affiliations =
                                    AcademicOnlineDepartmentParser
                                        .GetDepartmentAffiliations(
                                            profileDocument.RootElement);

                                affiliationsObserved +=
                                    affiliations.Count;

                                var affiliationSummary =
                                    affiliations.Count == 0
                                        ? "(none)"
                                        : string.Join(
                                            " | ",
                                            affiliations.Select(item =>
                                                $"Name={item.Name};" +
                                                $"ParentName={item.ParentName ?? "(null)"};" +
                                                $"Slug={item.Slug ?? "(null)"};" +
                                                $"ParentSlug={item.ParentSlug ?? "(null)"}"));

                                groupDiagnostics.Add(
                                    $"Anchor={anchor.ScholarSearchText}; " +
                                    $"AuthorId={candidate.AuthorId}; " +
                                    $"Affiliations={affiliationSummary}");

                                var parentIds =
                                    affiliations
                                        .Where(item =>
                                            DepartmentNamesEquivalent(
                                                item.Name,
                                                normalizedParentName) &&
                                            !string.IsNullOrWhiteSpace(
                                                item.Id))
                                        .Select(item => item.Id!)
                                        .ToHashSet(
                                            StringComparer.OrdinalIgnoreCase);

                                var parentSlugs =
                                    affiliations
                                        .Where(item =>
                                            DepartmentNamesEquivalent(
                                                item.Name,
                                                normalizedParentName) &&
                                            !string.IsNullOrWhiteSpace(
                                                item.Slug))
                                        .Select(item => item.Slug!)
                                        .ToHashSet(
                                            StringComparer.OrdinalIgnoreCase);

                                var directChildren =
                                    affiliations
                                        .Where(item =>
                                            !DepartmentNamesEquivalent(
                                                item.Name,
                                                normalizedParentName) &&
                                            (
                                                DepartmentNamesEquivalent(
                                                    item.ParentName,
                                                    normalizedParentName)
                                                ||
                                                (!string.IsNullOrWhiteSpace(
                                                     item.ParentId) &&
                                                 parentIds.Contains(
                                                     item.ParentId))
                                                ||
                                                (!string.IsNullOrWhiteSpace(
                                                     item.ParentSlug) &&
                                                 parentSlugs.Contains(
                                                     item.ParentSlug))
                                            ))
                                        .ToList();

                                var anchorMatch =
                                    directChildren
                                        .FirstOrDefault(item =>
                                            group.Any(seed =>
                                                DepartmentNamesEquivalent(
                                                    item.Name,
                                                    seed.SearchName)))
                                    ??
                                    directChildren
                                        .FirstOrDefault(item =>
                                            DepartmentNamesEquivalent(
                                                item.Name,
                                                group.Key));

                                if (anchorMatch is null)
                                {
                                    continue;
                                }

                                resolved =
                                    new AcademicOnlineDepartmentDiscoveryItem
                                    {
                                        AcademicOnlineDepartmentId =
                                            anchorMatch.Id,
                                        DepartmentName =
                                            anchorMatch.Name,
                                        Slug =
                                            anchorMatch.Slug,
                                        ParentAcademicOnlineDepartmentId =
                                            anchorMatch.ParentId,
                                        ParentName =
                                            anchorMatch.ParentName
                                            ?? normalizedParentName,
                                        ParentSlug =
                                            anchorMatch.ParentSlug,
                                        ScholarCount =
                                            Math.Max(
                                                1,
                                                identityCandidates.Count)
                                    };

                                break;
                            }
                            catch (HttpRequestException ex)
                            {
                                profileValidationFailures++;

                                groupDiagnostics.Add(
                                    $"Anchor={anchor.ScholarSearchText}; " +
                                    $"AuthorId={candidate.AuthorId}; " +
                                    $"ProfileError={ex.Message}");
                            }
                            catch (TaskCanceledException ex)
                                when (!cancellationToken.IsCancellationRequested)
                            {
                                profileValidationFailures++;

                                groupDiagnostics.Add(
                                    $"Anchor={anchor.ScholarSearchText}; " +
                                    $"AuthorId={candidate.AuthorId}; " +
                                    $"ProfileTimeout={ex.Message}");
                            }
                        }

                        if (resolved is not null)
                        {
                            break;
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        profileValidationFailures++;

                        groupDiagnostics.Add(
                            $"Anchor={anchor.ScholarSearchText}; " +
                            $"SearchError={ex.Message}");
                    }
                    catch (TaskCanceledException ex)
                        when (!cancellationToken.IsCancellationRequested)
                    {
                        profileValidationFailures++;

                        groupDiagnostics.Add(
                            $"Anchor={anchor.ScholarSearchText}; " +
                            $"SearchTimeout={ex.Message}");
                    }
                }
            }

            if (resolved is null)
            {
                departmentsUnresolved++;

                unresolvedDiagnostics.Add(
                    $"OfficialDepartment={group.Key}; " +
                    $"Attempts=[{string.Join(" || ", groupDiagnostics)}]");

                continue;
            }

            departments.Add(resolved);
        }

        return new AcademicOnlineDepartmentDiscoveryCollection
        {
            Departments =
                departments
                    .GroupBy(
                        item =>
                            !string.IsNullOrWhiteSpace(
                                item.AcademicOnlineDepartmentId)
                                ? "id:" +
                                  item.AcademicOnlineDepartmentId
                                : !string.IsNullOrWhiteSpace(
                                      item.Slug)
                                    ? "slug:" + item.Slug
                                    : "name:" +
                                      item.DepartmentName,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(
                        item => item.DepartmentName,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            PagesFetched = searchRequests,
            RowsPerPage = 5,
            ScholarsScanned = seenAuthorIds.Count,
            AffiliationsObserved = affiliationsObserved,
            ProfilesFetched = profilesFetched,
            ProfileValidationFailures =
                profileValidationFailures,
            DepartmentsUnresolved =
                departmentsUnresolved,
            UnresolvedDiagnostics =
                unresolvedDiagnostics,
            HitMaxPages = false
        };
    }

    public async Task<AcademicOnlineDepartmentScholarCollection> GetDepartmentScholarsAsync(
        string departmentName,
        int rowsPerPage = 100,
        int maxPages = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(departmentName))
        {
            throw new ArgumentException(
                "Academic Online department name is required.",
                nameof(departmentName));
        }

        var pageSize = Math.Clamp(rowsPerPage, 1, 100);
        var pageLimit = Math.Clamp(maxPages, 1, 1000);
        var scholars = new List<AcademicOnlineScholarSearchCandidate>();
        var seenAuthorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pagesFetched = 0;
        var hitMaxPages = false;

        for (var page = 0; page < pageLimit; page++)
        {
            using var document = await SearchScholarsAsync(
                facultyDisplayName: string.Empty,
                departmentName: departmentName.Trim(),
                page: page,
                rowsPerPage: pageSize,
                cancellationToken: cancellationToken);

            pagesFetched++;

            if (!document.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var pageRowCount = 0;
            var uniqueBefore = scholars.Count;

            foreach (var item in results.EnumerateArray())
            {
                pageRowCount++;

                var authorId = GetString(item, "id");
                if (string.IsNullOrWhiteSpace(authorId) ||
                    !seenAuthorIds.Add(authorId.Trim()))
                {
                    continue;
                }

                scholars.Add(new AcademicOnlineScholarSearchCandidate
                {
                    AuthorId = authorId.Trim(),
                    Name = GetString(item, "name"),
                    Email = GetString(item, "email"),
                    Position = GetString(item, "position"),
                    Department =
                        AcademicOnlineDepartmentParser
                            .GetPreferredDepartmentName(item),
                    Institution =
                        GetString(item, "institution")
                        ?? GetString(item, "institution_name"),
                    OrcidUrl =
                        GetString(item, "orcid_url")
                        ?? GetString(item, "orcid"),
                    RankingScore = GetDecimal(item, "ranking_score")
                });
            }

            var newUniqueRows = scholars.Count - uniqueBefore;
            if (pageRowCount == 0 || newUniqueRows == 0)
            {
                break;
            }

            if (page == pageLimit - 1)
            {
                hitMaxPages = true;
            }
        }

        return new AcademicOnlineDepartmentScholarCollection
        {
            Scholars = scholars
                .OrderBy(item => item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PagesFetched = pagesFetched,
            RowsPerPage = pageSize,
            HitMaxPages = hitMaxPages
        };
    }

    public Task<JsonDocument> GetAuthorGeneralAsync(string authorId, CancellationToken cancellationToken = default)
    {
        return GetJsonDocumentAsync("author/general?authorId=" + Uri.EscapeDataString(authorId), cancellationToken);
    }

    public Task<JsonDocument> GetAuthorPublicationsAsync(
        string authorId,
        bool verifiedOnly,
        int page,
        int rowsPerPage,
        int? fromYear = null,
        int? toYear = null,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["authorId"] = authorId,
            ["verify"] = verifiedOnly ? "true" : "false",
            ["page"] = page.ToString(),
            ["rowsPerPage"] = rowsPerPage.ToString(),
            ["order_by"] = "publication_year",
            ["order"] = "desc"
        };

        if (fromYear.HasValue)
        {
            query["fromYear"] = fromYear.Value.ToString();
        }

        if (toYear.HasValue)
        {
            query["toYear"] = toYear.Value.ToString();
        }

        return GetJsonDocumentAsync("author/publications?" + BuildQueryString(query), cancellationToken);
    }

    public async Task<AcademicOnlinePublicationCollection> GetAllAuthorPublicationsAsync(
        string authorId,
        bool verifiedOnly,
        int? rowsPerPage = null,
        int? maxPages = null,
        int? fromYear = null,
        int? toYear = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authorId))
        {
            throw new ArgumentException("Academic Online authorId is required.", nameof(authorId));
        }

        var pageSize = Math.Clamp(rowsPerPage ?? _options.PublicationRowsPerPage, 1, 100);
        var pageLimit = Math.Clamp(maxPages ?? _options.MaxPublicationPages, 1, 1000);

        var publications = new List<AcademicOnlinePublicationItem>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pagesFetched = 0;
        var hitMaxPages = false;

        for (var page = 0; page < pageLimit; page++)
        {
            using var document = await GetAuthorPublicationsAsync(
                authorId,
                verifiedOnly,
                page,
                pageSize,
                fromYear,
                toYear,
                cancellationToken);

            pagesFetched++;

            if (!document.RootElement.TryGetProperty("publications", out var array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "Academic Online author/publications response did not contain a publications array.");
            }

            var pageCount = 0;
            var publicationCountBeforePage = publications.Count;

            foreach (var publication in array.EnumerateArray())
            {
                pageCount++;

                var item = ParsePublication(publication);
                var key = BuildPublicationDeduplicationKey(item, page, pageCount);

                if (seenKeys.Add(key))
                {
                    publications.Add(item);
                }
            }

            var newUniquePublications = publications.Count - publicationCountBeforePage;

            // Do not assume the service honors the requested page size. Continue until
            // Academic Online returns no rows or stops yielding new unique publications.
            if (pageCount == 0 || newUniquePublications == 0)
            {
                break;
            }

            if (page == pageLimit - 1)
            {
                hitMaxPages = true;
            }
        }

        return new AcademicOnlinePublicationCollection
        {
            Publications = publications
                .OrderByDescending(item => item.PublicationYear ?? int.MinValue)
                .ThenBy(item => item.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PagesFetched = pagesFetched,
            RowsPerPage = pageSize,
            HitMaxPages = hitMaxPages
        };
    }

    private async Task<JsonDocument> GetJsonDocumentAsync(string relativeUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        AddAuthorization(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Academic Online returned an empty JSON document.");
    }

    private async Task<JsonDocument> PostJsonDocumentAsync(string relativeUri, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUri)
        {
            Content = JsonContent.Create(body)
        };
        AddAuthorization(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Academic Online returned an empty JSON document.");
    }

    private static AcademicOnlinePublicationItem ParsePublication(JsonElement publication)
    {
        var yearText = GetString(publication, "year");

        return new AcademicOnlinePublicationItem
        {
            Id = GetString(publication, "id"),
            Title = GetString(publication, "title"),
            Venue = GetString(publication, "venue"),
            YearText = yearText,
            PublicationYear = int.TryParse(yearText, out var year) ? year : null,
            CitationCount = GetInt(publication, "num_citations"),
            Doi = GetString(publication, "doi"),
            OpenAlexId = GetString(publication, "openalex_id"),
            Verified = GetBool(publication, "verified"),
            Link = GetString(publication, "link"),
            CoauthorsJson = GetRawJson(publication, "coauthors"),
            KeywordsJson = GetRawJson(publication, "keywords"),
            RawJson = publication.GetRawText()
        };
    }

    private static string BuildPublicationDeduplicationKey(
        AcademicOnlinePublicationItem item,
        int page,
        int ordinal)
    {
        if (!string.IsNullOrWhiteSpace(item.Id))
        {
            return "id:" + item.Id.Trim();
        }

        if (!string.IsNullOrWhiteSpace(item.OpenAlexId))
        {
            return "openalex:" + item.OpenAlexId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(item.Doi))
        {
            return "doi:" + item.Doi.Trim();
        }

        if (!string.IsNullOrWhiteSpace(item.Title))
        {
            return "title-year:" + item.Title.Trim() + "|" + (item.PublicationYear?.ToString() ?? item.YearText ?? string.Empty);
        }

        return $"page-ordinal:{page}:{ordinal}";
    }

    private static bool PersonNameMatchesAnchor(
        string? candidateName,
        string? anchorName)
    {
        var candidateTokens =
            TokenizePersonName(candidateName);

        var anchorTokens =
            TokenizePersonName(anchorName)
                .Where(token =>
                    token.Length > 1)
                .ToList();

        if (candidateTokens.Count == 0 ||
            anchorTokens.Count == 0)
        {
            return false;
        }

        if (anchorTokens.Count == 1)
        {
            return candidateTokens.Contains(
                anchorTokens[0],
                StringComparer.Ordinal);
        }

        return
            candidateTokens.Contains(
                anchorTokens[0],
                StringComparer.Ordinal)
            &&
            candidateTokens.Contains(
                anchorTokens[^1],
                StringComparer.Ordinal);
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

    private static bool DepartmentNamesEquivalent(
        string? left,
        string? right)
    {
        return string.Equals(
            NormalizeDepartmentNameForComparison(left),
            NormalizeDepartmentNameForComparison(right),
            StringComparison.Ordinal);
    }

    private static string NormalizeDepartmentNameForComparison(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized =
            value.Trim().ToLowerInvariant();

        if (normalized.StartsWith(
                "department of ",
                StringComparison.Ordinal))
        {
            normalized =
                normalized["department of ".Length..];
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

    private static string BuildDepartmentDiscoveryKey(
        AcademicOnlineDepartmentAffiliation affiliation)
    {
        if (!string.IsNullOrWhiteSpace(affiliation.Id))
        {
            return "id:" + affiliation.Id.Trim();
        }

        if (!string.IsNullOrWhiteSpace(affiliation.Slug))
        {
            return "slug:" + affiliation.Slug.Trim();
        }

        return "name:" + affiliation.Name.Trim();
    }

    private sealed class CandidateDepartmentAccumulator
    {
        public CandidateDepartmentAccumulator(
            string departmentName,
            string representativeAuthorId)
        {
            DepartmentName = departmentName;
            RepresentativeAuthorId =
                representativeAuthorId;
        }

        public string DepartmentName { get; }
        public string RepresentativeAuthorId { get; }

        public HashSet<string> AuthorIds { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class DepartmentDiscoveryAccumulator
    {
        public DepartmentDiscoveryAccumulator(
            AcademicOnlineDepartmentAffiliation affiliation)
        {
            Affiliation = affiliation;
        }

        public AcademicOnlineDepartmentAffiliation Affiliation
        {
            get;
        }

        public HashSet<string> AuthorIds { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement element, string propertyName)
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

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.TryParse(value.ToString(), out var parsed)
            ? parsed
            : null;
    }

    private static bool? GetBool(JsonElement element, string propertyName)
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

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static string? GetRawJson(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return value.GetRawText();
    }

    private void AddAuthorization(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("AcademicOnline:ApiKey is missing. Store it in user-secrets or an environment variable; do not commit it to appsettings.json.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey.Trim());
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, string?> values)
    {
        return string.Join("&", values
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .Select(kvp => Uri.EscapeDataString(kvp.Key) + "=" + Uri.EscapeDataString(kvp.Value!)));
    }
}
