using System.Text;
using System.Text.Json;
using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.OpenAlexClient;

namespace OpenAlex.Web.Services;

public sealed class FacultyPublicationFreshnessService : IFacultyPublicationFreshnessService
{
    private const int MaxOpenAlexPages = 25;

    private readonly FacultyPublicationFreshnessRepository _freshnessRepository;
    private readonly FacultyPublicationRepository _publicationRepository;
    private readonly AcademicOnlineUniversalFacultyLookupService _academicOnlineLookup;
    private readonly AcademicOnlineApiClient _academicOnlineClient;
    private readonly OpenAlexApiClient _openAlexClient;

    public FacultyPublicationFreshnessService(
        FacultyPublicationFreshnessRepository freshnessRepository,
        FacultyPublicationRepository publicationRepository,
        AcademicOnlineUniversalFacultyLookupService academicOnlineLookup,
        AcademicOnlineApiClient academicOnlineClient,
        OpenAlexApiClient openAlexClient)
    {
        _freshnessRepository = freshnessRepository;
        _publicationRepository = publicationRepository;
        _academicOnlineLookup = academicOnlineLookup;
        _academicOnlineClient = academicOnlineClient;
        _openAlexClient = openAlexClient;
    }

    public async Task<FacultyPublicationFreshnessResult> ScanByUserIdAsync(
        string userId,
        int? requestedFromYear = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedUserId =
            AcademicOnlineUniversalFacultyLookupService
                .NormalizeUvaUserId(userId);

        var identities =
            await _freshnessRepository
                .GetFacultyIdentitiesByUvaUserIdAsync(
                    normalizedUserId,
                    cancellationToken);

        if (identities.Count == 0)
        {
            throw new InvalidOperationException(
                $"No current faculty record was found for UVA User ID '{normalizedUserId}'.");
        }

        var facultyKeys = identities
            .Select(item => item.FacultyKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var localTasks = facultyKeys
            .Select(key =>
                _publicationRepository.GetPublicationsAsync(
                    key,
                    top: 1000,
                    cancellationToken))
            .ToArray();

        var reviewTask =
            _freshnessRepository.GetReviewStatesByUvaUserIdAsync(
                normalizedUserId,
                cancellationToken);

        var localResults =
            await Task.WhenAll(localTasks);

        var reviewStates =
            await reviewTask;

        var localPublications = localResults
            .SelectMany(items => items)
            .ToArray();

        var currentYear = DateTime.UtcNow.Year;
        var latestLocalYear = localPublications
            .Where(item => item.PublicationYear.HasValue)
            .Select(item => item.PublicationYear!.Value)
            .DefaultIfEmpty(currentYear - 1)
            .Max();

        var fromYear =
            requestedFromYear ??
            Math.Max(1800, latestLocalYear - 1);

        if (fromYear < 1800 ||
            fromYear > currentYear + 1)
        {
            throw new InvalidOperationException(
                $"From year must be between 1800 and {currentYear + 1}.");
        }

        var toYear = currentYear + 1;

        var warnings = new List<string>();

        var academicOnlineAuthorIds = identities
            .Select(item => item.AcademicOnlineAuthorId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (academicOnlineAuthorIds.Count == 0)
        {
            try
            {
                var exactMatches =
                    await _academicOnlineLookup.SearchByUvaUserIdAsync(
                        normalizedUserId,
                        cancellationToken);

                academicOnlineAuthorIds.AddRange(
                    exactMatches
                        .Select(item => item.AuthorId)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add(
                    $"Academic Online identity fallback failed: {ex.Message}");
            }
        }

        academicOnlineAuthorIds = academicOnlineAuthorIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (academicOnlineAuthorIds.Count > 1)
        {
            warnings.Add(
                "More than one exact Academic Online author ID is associated with this UVA User ID. " +
                "The read-only scan includes all of them so the discrepancy can be reviewed safely.");
        }

        var openAlexAuthorIds = identities
            .SelectMany(item =>
            {
                if (string.Equals(
                        item.PublicationIdentitySource,
                        "OpenAlex",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new[]
                    {
                        NormalizeOpenAlexAuthorId(item.SourceAuthorId)
                    };
                }

                if (IsUsableOpenAlexResolutionStatus(
                        item.OpenAlexResolutionStatus))
                {
                    return new[]
                    {
                        NormalizeOpenAlexAuthorId(item.OpenAlexAuthorId)
                    };
                }

                return Array.Empty<string?>();
            })
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var skippedUntrustedOpenAlexIdentity = identities.Any(item =>
            !string.Equals(
                item.PublicationIdentitySource,
                "OpenAlex",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.OpenAlexAuthorId) &&
            !IsUsableOpenAlexResolutionStatus(
                item.OpenAlexResolutionStatus));

        if (skippedUntrustedOpenAlexIdentity)
        {
            warnings.Add(
                "A stored OpenAlex roster identity was not used because its resolution status is not an accepted identity state. " +
                "The scanner will prefer an exact ORCID fallback instead of trusting ambiguous identity data.");
        }

        if (openAlexAuthorIds.Count == 0 &&
            academicOnlineAuthorIds.Count > 0)
        {
            var orcidFallback =
                await ResolveOpenAlexAuthorsFromAcademicOnlineOrcidAsync(
                    academicOnlineAuthorIds,
                    cancellationToken);

            openAlexAuthorIds.AddRange(
                orcidFallback.OpenAlexAuthorIds);

            warnings.AddRange(
                orcidFallback.Warnings);
        }

        openAlexAuthorIds = openAlexAuthorIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (openAlexAuthorIds.Count > 1)
        {
            warnings.Add(
                "More than one trusted OpenAlex author ID is associated with this UVA User ID. " +
                "The read-only scan includes all resolved IDs and does not change identity data.");
        }

        if (openAlexAuthorIds.Count == 0)
        {
            warnings.Add(
                "No trusted OpenAlex author ID could be resolved from the local roster or an exact ORCID match. " +
                "OpenAlex is not name-matched automatically on this page because that could attach another researcher's works.");
        }

        var academicOnlineTask =
            ScanAcademicOnlineAsync(
                academicOnlineAuthorIds,
                fromYear,
                toYear,
                cancellationToken);

        var openAlexTask =
            ScanOpenAlexAsync(
                openAlexAuthorIds,
                fromYear,
                toYear,
                cancellationToken);

        await Task.WhenAll(
            academicOnlineTask,
            openAlexTask);

        var academicOnlineScan =
            await academicOnlineTask;

        var openAlexScan =
            await openAlexTask;

        var accumulators =
            new List<PublicationAccumulator>();

        foreach (var item in localPublications
                     .Where(item =>
                         !item.PublicationYear.HasValue ||
                         item.PublicationYear.Value >= fromYear))
        {
            AddPublication(
                accumulators,
                new ScannedPublication
                {
                    Title = item.Title,
                    Venue = item.Venue,
                    PublicationYear = item.PublicationYear,
                    Doi = item.Doi,
                    OpenAlexWorkId =
                        TryGetOpenAlexWorkIdFromLink(item.Link),
                    Link = item.Link
                },
                PublicationObservation.Local);
        }

        foreach (var item in academicOnlineScan.Publications)
        {
            AddPublication(
                accumulators,
                item,
                PublicationObservation.AcademicOnline);
        }

        foreach (var item in openAlexScan.Publications)
        {
            AddPublication(
                accumulators,
                item,
                PublicationObservation.OpenAlex);
        }

        foreach (var review in reviewStates
                     .Where(item =>
                         !item.PublicationYear.HasValue ||
                         item.PublicationYear.Value >= fromYear))
        {
            AddReviewState(
                accumulators,
                review);
        }

        var items = accumulators
            .Select(ToResultItem)
            .OrderByDescending(item =>
                item.PublicationYear ?? int.MinValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var firstIdentity = identities[0];

        return new FacultyPublicationFreshnessResult
        {
            UvaUserId = normalizedUserId,
            FacultyName = firstIdentity.DisplayName,
            Email = identities
                .Select(item => item.Email)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Departments = identities
                .Select(item => item.DepartmentName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            FromYear = fromYear,
            ToYear = toYear,
            CheckedAtUtc = DateTime.UtcNow,
            AcademicOnlineAuthorIds = academicOnlineAuthorIds,
            OpenAlexAuthorIds = openAlexAuthorIds,
            LocalPublicationCount = items.Count(item => item.ExistsLocally),
            AcademicOnlinePublicationCount = academicOnlineScan.Publications.Count,
            OpenAlexPublicationCount = openAlexScan.Publications.Count,
            AcademicOnlineSource = academicOnlineScan.SourceStatus,
            OpenAlexSource = openAlexScan.SourceStatus,
            Warnings = warnings,
            Items = items
        };
    }

    public async Task<PublicationDoiLookupResult> LookupDoiAsync(
        string doi,
        CancellationToken cancellationToken = default)
    {
        var normalizedDoi = NormalizeDoi(doi);

        if (normalizedDoi is null ||
            !normalizedDoi.StartsWith(
                "10.",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Enter a DOI such as 10.1000/example. You may also paste a https://doi.org/ URL.");
        }

        var warnings = new List<string>();
        PublicationDoiWork? publication = null;
        IReadOnlyList<PublicationDoiOpenAlexAuthor> authors =
            Array.Empty<PublicationDoiOpenAlexAuthor>();
        var openAlexAvailable = true;
        string? openAlexMessage = null;

        try
        {
            using var page =
                await _openAlexClient.GetWorksPageAsync(
                    $"doi:{normalizedDoi}",
                    "*",
                    cancellationToken);

            var matches = page.ResultsElement
                .EnumerateArray()
                .Where(work =>
                    string.Equals(
                        NormalizeDoi(GetString(work, "doi")),
                        normalizedDoi,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length == 0)
            {
                openAlexMessage =
                    "OpenAlex did not return an exact DOI match at scan time.";
            }
            else
            {
                if (matches.Length > 1)
                {
                    warnings.Add(
                        "OpenAlex returned more than one exact DOI record. The first result is displayed and no identity data is changed.");
                }

                var work = matches[0];

                publication = new PublicationDoiWork
                {
                    Title =
                        GetString(work, "title") ??
                        GetString(work, "display_name") ??
                        "(Untitled publication)",
                    Venue =
                        GetNestedString(
                            work,
                            "primary_location",
                            "source",
                            "display_name"),
                    PublicationYear =
                        GetInt(work, "publication_year"),
                    PublicationDate =
                        GetString(work, "publication_date"),
                    Doi = normalizedDoi,
                    OpenAlexWorkId =
                        NormalizeOpenAlexWorkId(
                            GetString(work, "id")),
                    WorkType =
                        GetString(work, "type"),
                    Link =
                        GetNestedString(
                            work,
                            "primary_location",
                            "landing_page_url") ??
                        GetString(work, "doi") ??
                        GetString(work, "id")
                };

                authors = ReadOpenAlexAuthors(work);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            openAlexAvailable = false;
            openAlexMessage =
                $"OpenAlex DOI lookup failed: {ex.Message}";
        }

        var openAlexAuthorIds = authors
            .Select(item => item.OpenAlexAuthorId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var reviewAssociationsTask =
            _freshnessRepository.GetReviewAssociationsByDoiAsync(
                normalizedDoi,
                cancellationToken);

        var trustedFacultyTask =
            _freshnessRepository.GetUvaUserIdsByTrustedOpenAlexAuthorIdsAsync(
                openAlexAuthorIds,
                cancellationToken);

        await Task.WhenAll(
            reviewAssociationsTask,
            trustedFacultyTask);

        var reviewAssociations =
            await reviewAssociationsTask;

        var trustedFacultyUserIds =
            await trustedFacultyTask;

        var reviewFacultyUserIds = reviewAssociations
            .Select(item => item.UvaUserId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var trustedFacultySet = trustedFacultyUserIds
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidateUserIds = trustedFacultyUserIds
            .Concat(reviewFacultyUserIds)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (publication is not null &&
            candidateUserIds.Length == 0 &&
            authors.Any(item => item.HasUvaAffiliationHint))
        {
            warnings.Add(
                "OpenAlex reports a University of Virginia affiliation on at least one author, but no trusted local faculty identity matched that OpenAlex author ID. The page does not assign faculty by name automatically.");
        }

        var facultyCandidates = new List<PublicationDoiFacultyCandidate>();

        foreach (var userId in candidateUserIds)
        {
            var reviewForFaculty = reviewAssociations
                .Where(item =>
                    string.Equals(
                        item.UvaUserId,
                        userId,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var reviewIdentity =
                trustedFacultySet.Contains(userId)
                    ? await ResolveUniqueTrustedReviewIdentityAsync(
                        userId,
                        openAlexAuthorIds,
                        cancellationToken)
                    : null;

            var anchorYear =
                publication?.PublicationYear ??
                reviewForFaculty
                    .Where(item => item.PublicationYear.HasValue)
                    .Select(item => item.PublicationYear!.Value)
                    .DefaultIfEmpty(DateTime.UtcNow.Year)
                    .Max();

            try
            {
                var scan =
                    await ScanByUserIdAsync(
                        userId,
                        Math.Max(1800, anchorYear - 1),
                        cancellationToken);

                var item = scan.Items
                    .FirstOrDefault(candidate =>
                        string.Equals(
                            NormalizeDoi(candidate.Doi),
                            normalizedDoi,
                            StringComparison.OrdinalIgnoreCase));

                facultyCandidates.Add(
                    new PublicationDoiFacultyCandidate
                    {
                        UvaUserId = scan.UvaUserId,
                        FacultyName = scan.FacultyName,
                        Departments = scan.Departments,
                        AssociationBasis = BuildAssociationBasis(
                            trustedFacultySet.Contains(userId),
                            reviewFacultyUserIds.Contains(userId)),
                        HasTrustedOpenAlexIdentity =
                            trustedFacultySet.Contains(userId),
                        ReviewDepartmentId =
                            reviewIdentity?.DepartmentId,
                        ReviewDepartmentName =
                            reviewIdentity?.DepartmentName,
                        ExistsLocally = item?.ExistsLocally ?? false,
                        ExistsInAcademicOnline =
                            item?.ExistsInAcademicOnline ?? false,
                        ExistsInOpenAlex =
                            item?.ExistsInOpenAlex ??
                            trustedFacultySet.Contains(userId),
                        ReviewStatus =
                            item?.ReviewStatus ??
                            GetBestReviewStatus(reviewForFaculty),
                        StatusLabel =
                            item?.StatusLabel ??
                            BuildFallbackDoiStatus(reviewForFaculty),
                        AcademicOnlineWasAttempted =
                            scan.AcademicOnlineSource.WasAttempted,
                        AcademicOnlineIsAvailable =
                            scan.AcademicOnlineSource.IsAvailable,
                        OpenAlexWasAttempted =
                            scan.OpenAlexSource.WasAttempted,
                        OpenAlexIsAvailable =
                            scan.OpenAlexSource.IsAvailable,
                        ScanWarnings = scan.Warnings
                    });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                facultyCandidates.Add(
                    new PublicationDoiFacultyCandidate
                    {
                        UvaUserId = userId,
                        AssociationBasis = BuildAssociationBasis(
                            trustedFacultySet.Contains(userId),
                            reviewFacultyUserIds.Contains(userId)),
                        HasTrustedOpenAlexIdentity =
                            trustedFacultySet.Contains(userId),
                        ReviewDepartmentId =
                            reviewIdentity?.DepartmentId,
                        ReviewDepartmentName =
                            reviewIdentity?.DepartmentName,
                        ReviewStatus =
                            GetBestReviewStatus(reviewForFaculty),
                        StatusLabel =
                            BuildFallbackDoiStatus(reviewForFaculty),
                        ScanError = ex.Message
                    });
            }
        }

        if (publication is null &&
            reviewAssociations.Count == 0)
        {
            warnings.Add(
                "No exact OpenAlex DOI record or existing local review record was found. Academic Online is checked through a resolved faculty identity rather than by a global DOI search, so enter the faculty member's UVA User ID if you need to check Academic Online for this DOI.");
        }

        return new PublicationDoiLookupResult
        {
            Doi = normalizedDoi,
            CheckedAtUtc = DateTime.UtcNow,
            OpenAlexWasAttempted = true,
            OpenAlexIsAvailable = openAlexAvailable,
            OpenAlexMessage = openAlexMessage,
            Publication = publication,
            OpenAlexAuthors = authors,
            FacultyCandidates = facultyCandidates,
            Warnings = warnings
        };
    }

    private static IReadOnlyList<PublicationDoiOpenAlexAuthor>
        ReadOpenAlexAuthors(
            JsonElement work)
    {
        if (work.ValueKind != JsonValueKind.Object ||
            !work.TryGetProperty("authorships", out var authorships) ||
            authorships.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<PublicationDoiOpenAlexAuthor>();
        }

        var authors = new List<PublicationDoiOpenAlexAuthor>();

        foreach (var authorship in authorships.EnumerateArray())
        {
            if (authorship.ValueKind != JsonValueKind.Object ||
                !authorship.TryGetProperty("author", out var author) ||
                author.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var institutionNames = new List<string>();

            if (authorship.TryGetProperty("institutions", out var institutions) &&
                institutions.ValueKind == JsonValueKind.Array)
            {
                foreach (var institution in institutions.EnumerateArray())
                {
                    var name = GetString(institution, "display_name");

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        institutionNames.Add(name.Trim());
                    }
                }
            }

            var distinctInstitutions = institutionNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            authors.Add(
                new PublicationDoiOpenAlexAuthor
                {
                    OpenAlexAuthorId =
                        NormalizeOpenAlexAuthorId(
                            GetString(author, "id")),
                    DisplayName =
                        GetString(author, "display_name") ??
                        "(Unnamed author)",
                    Orcid =
                        NormalizeOrcid(
                            GetString(author, "orcid")),
                    InstitutionNames = distinctInstitutions,
                    HasUvaAffiliationHint = distinctInstitutions.Any(name =>
                        name.Contains(
                            "University of Virginia",
                            StringComparison.OrdinalIgnoreCase))
                });
        }

        return authors;
    }

    private static string BuildAssociationBasis(
        bool trustedOpenAlexIdentity,
        bool existingReviewRecord)
    {
        if (trustedOpenAlexIdentity && existingReviewRecord)
        {
            return "Trusted OpenAlex author identity + existing review record";
        }

        if (trustedOpenAlexIdentity)
        {
            return "Trusted OpenAlex author identity";
        }

        if (existingReviewRecord)
        {
            return "Existing review record";
        }

        return "Faculty association found";
    }

    private static string? GetBestReviewStatus(
        IReadOnlyList<FacultyPublicationDoiReviewAssociation> associations)
    {
        return associations
            .Select(item =>
                !string.IsNullOrWhiteSpace(item.DecisionStatus)
                    ? item.DecisionStatus
                    : item.ReviewStatus)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string BuildFallbackDoiStatus(
        IReadOnlyList<FacultyPublicationDoiReviewAssociation> associations)
    {
        if (associations.Any(item =>
                string.Equals(
                    item.DecisionStatus,
                    "Accepted",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return "Accepted review record";
        }

        if (associations.Any(item =>
                string.Equals(
                    item.DecisionStatus,
                    "Rejected",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return "Previously rejected";
        }

        if (associations.Any(item =>
                string.Equals(
                    item.ReviewStatus,
                    "Pending",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return "Pending review";
        }

        return "Faculty association found; DOI not returned in the faculty scan window";
    }


    private async Task<FacultyPublicationFreshnessIdentity?>
        ResolveUniqueTrustedReviewIdentityAsync(
            string uvaUserId,
            IReadOnlyCollection<string> doiOpenAlexAuthorIds,
            CancellationToken cancellationToken)
    {
        if (doiOpenAlexAuthorIds.Count == 0)
        {
            return null;
        }

        var doiAuthorSet = doiOpenAlexAuthorIds
            .Select(NormalizeOpenAlexAuthorId)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (doiAuthorSet.Count == 0)
        {
            return null;
        }

        var identities =
            await _freshnessRepository
                .GetFacultyIdentitiesByUvaUserIdAsync(
                    uvaUserId,
                    cancellationToken);

        var matches = identities
            .Where(identity =>
            {
                var trustedAuthorId =
                    string.Equals(
                        identity.PublicationIdentitySource,
                        "OpenAlex",
                        StringComparison.OrdinalIgnoreCase)
                        ? NormalizeOpenAlexAuthorId(
                            identity.SourceAuthorId)
                        : IsUsableOpenAlexResolutionStatus(
                            identity.OpenAlexResolutionStatus)
                            ? NormalizeOpenAlexAuthorId(
                                identity.OpenAlexAuthorId)
                            : null;

                return trustedAuthorId is not null &&
                       doiAuthorSet.Contains(trustedAuthorId);
            })
            .GroupBy(identity => new
            {
                identity.FacultyKey,
                identity.DepartmentId
            })
            .Select(group => group.First())
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : null;
    }


    public async Task<FacultyPublicationManualReviewSubmissionResult>
        SendDoiToReviewAsync(
            string doi,
            string uvaUserId,
            long expectedDepartmentId,
            string submittedBy,
            long submittedByAppUserId,
            CancellationToken cancellationToken = default)
    {
        if (submittedByAppUserId <= 0 || string.IsNullOrWhiteSpace(submittedBy))
            throw new UnauthorizedAccessException("A current authenticated application account is required.");

        var normalizedDoi = NormalizeDoi(doi);

        if (normalizedDoi is null ||
            !normalizedDoi.StartsWith(
                "10.",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A valid DOI is required before a publication can be sent to review.");
        }

        var normalizedUserId =
            AcademicOnlineUniversalFacultyLookupService
                .NormalizeUvaUserId(uvaUserId);

        if (expectedDepartmentId <= 0)
        {
            throw new InvalidOperationException(
                "A valid department is required before a publication can be sent to review.");
        }

        var lookup =
            await LookupDoiAsync(
                normalizedDoi,
                cancellationToken);

        var publication =
            lookup.Publication ??
            throw new InvalidOperationException(
                "The DOI is not currently available as an exact OpenAlex work, so it cannot be submitted automatically.");

        var openAlexWorkId =
            NormalizeOpenAlexWorkId(
                publication.OpenAlexWorkId) ??
            throw new InvalidOperationException(
                "The OpenAlex DOI result did not contain a valid work ID.");

        var facultyCandidate =
            lookup.FacultyCandidates
                .SingleOrDefault(item =>
                    string.Equals(
                        item.UvaUserId,
                        normalizedUserId,
                        StringComparison.OrdinalIgnoreCase))
            ??
            throw new InvalidOperationException(
                "The selected UVA faculty member is no longer a trusted association for this DOI. Run the DOI lookup again.");

        if (!facultyCandidate.HasTrustedOpenAlexIdentity)
        {
            throw new InvalidOperationException(
                "Only a trusted OpenAlex author identity can be used to create a manual review candidate.");
        }

        if (facultyCandidate.ExistsLocally)
        {
            throw new InvalidOperationException(
                "This publication is already present in the faculty member's local publication set.");
        }

        if (!string.IsNullOrWhiteSpace(
                facultyCandidate.ReviewStatus))
        {
            throw new InvalidOperationException(
                $"This publication is already represented in the review workflow with status '{facultyCandidate.ReviewStatus}'.");
        }

        var doiAuthorIds =
            lookup.OpenAlexAuthors
                .Select(item =>
                    NormalizeOpenAlexAuthorId(
                        item.OpenAlexAuthorId))
                .Where(value =>
                    value is not null)
                .Select(value => value!)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        if (doiAuthorIds.Count == 0)
        {
            throw new InvalidOperationException(
                "The OpenAlex work does not expose a usable author identity for safe faculty association.");
        }

        var identities =
            await _freshnessRepository
                .GetFacultyIdentitiesByUvaUserIdAsync(
                    normalizedUserId,
                    cancellationToken);

        var trustedIdentityMatches = identities
            .Select(identity =>
            {
                var trustedAuthorId =
                    string.Equals(
                        identity.PublicationIdentitySource,
                        "OpenAlex",
                        StringComparison.OrdinalIgnoreCase)
                        ? NormalizeOpenAlexAuthorId(
                            identity.SourceAuthorId)
                        : IsUsableOpenAlexResolutionStatus(
                            identity.OpenAlexResolutionStatus)
                            ? NormalizeOpenAlexAuthorId(
                                identity.OpenAlexAuthorId)
                            : null;

                return new
                {
                    Identity = identity,
                    OpenAlexAuthorId = trustedAuthorId
                };
            })
            .Where(item =>
                item.OpenAlexAuthorId is not null &&
                doiAuthorIds.Contains(
                    item.OpenAlexAuthorId))
            .GroupBy(
                item => new
                {
                    item.Identity.FacultyKey,
                    item.Identity.DepartmentId,
                    OpenAlexAuthorId =
                        item.OpenAlexAuthorId!
                })
            .Select(group =>
                group.First())
            .ToArray();

        if (trustedIdentityMatches.Length == 0)
        {
            throw new InvalidOperationException(
                "The trusted OpenAlex author association could not be revalidated against the current faculty identity records.");
        }

        if (trustedIdentityMatches.Length > 1)
        {
            throw new InvalidOperationException(
                "More than one department/faculty identity matches this DOI. PATCH022C will not choose a department automatically; resolve the faculty identity before submitting this publication.");
        }

        var match = trustedIdentityMatches[0];

        if (match.Identity.DepartmentId != expectedDepartmentId)
        {
            throw new InvalidOperationException(
                "The selected department no longer matches the trusted faculty identity for this DOI. Run the DOI lookup again before submitting.");
        }

        return await _freshnessRepository
            .SubmitDoiReviewCandidateAsync(
                match.Identity,
                match.OpenAlexAuthorId!,
                openAlexWorkId,
                normalizedDoi,
                publication.Title,
                publication.Venue,
                publication.PublicationYear,
                publication.Link,
                submittedBy,
                submittedByAppUserId,
                cancellationToken);
    }

    private async Task<OpenAlexIdentityFallbackResult>
        ResolveOpenAlexAuthorsFromAcademicOnlineOrcidAsync(
            IReadOnlyList<string> academicOnlineAuthorIds,
            CancellationToken cancellationToken)
    {
        var openAlexAuthorIds = new List<string>();
        var warnings = new List<string>();

        foreach (var academicOnlineAuthorId in academicOnlineAuthorIds)
        {
            try
            {
                using var general =
                    await _academicOnlineClient.GetAuthorGeneralAsync(
                        academicOnlineAuthorId,
                        cancellationToken);

                var orcid =
                    NormalizeOrcid(
                        GetString(general.RootElement, "orcid_url") ??
                        GetString(general.RootElement, "orcid"));

                if (orcid is null)
                {
                    continue;
                }

                using var page =
                    await _openAlexClient.GetAuthorsFilterPageAsync(
                        $"orcid:{orcid}",
                        perPage: 10,
                        cancellationToken: cancellationToken);

                var matches = page.ResultsElement
                    .EnumerateArray()
                    .Select(item =>
                        NormalizeOpenAlexAuthorId(
                            GetString(item, "id")))
                    .Where(value => value is not null)
                    .Select(value => value!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (matches.Length == 1)
                {
                    openAlexAuthorIds.Add(matches[0]);
                }
                else if (matches.Length > 1)
                {
                    warnings.Add(
                        $"ORCID {orcid} returned more than one OpenAlex author record, so the ORCID fallback was not used automatically.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add(
                    $"OpenAlex ORCID identity fallback failed for Academic Online author {academicOnlineAuthorId}: {ex.Message}");
            }
        }

        return new OpenAlexIdentityFallbackResult
        {
            OpenAlexAuthorIds = openAlexAuthorIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Warnings = warnings
        };
    }

    private async Task<SourceScanResult> ScanAcademicOnlineAsync(
        IReadOnlyList<string> authorIds,
        int fromYear,
        int toYear,
        CancellationToken cancellationToken)
    {
        if (authorIds.Count == 0)
        {
            return new SourceScanResult
            {
                SourceStatus = new PublicationFreshnessSourceStatus
                {
                    SourceName = "Academic Online",
                    WasAttempted = false,
                    IsAvailable = false,
                    Message = "No exact Academic Online identity was available."
                }
            };
        }

        var publications = new List<ScannedPublication>();
        var errors = new List<string>();
        var pagesFetched = 0;
        var hitMaxPages = false;

        foreach (var authorId in authorIds)
        {
            try
            {
                var result =
                    await _academicOnlineClient
                        .GetAllAuthorPublicationsAsync(
                            authorId,
                            verifiedOnly: false,
                            fromYear: fromYear,
                            toYear: toYear,
                            cancellationToken: cancellationToken);

                pagesFetched += result.PagesFetched;
                hitMaxPages |= result.HitMaxPages;

                publications.AddRange(
                    result.Publications.Select(item =>
                        new ScannedPublication
                        {
                            Title = item.Title ?? string.Empty,
                            Venue = item.Venue,
                            PublicationYear = item.PublicationYear,
                            Doi = item.Doi,
                            OpenAlexWorkId = item.OpenAlexId,
                            Link = item.Link
                        }));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(
                    $"{authorId}: {ex.Message}");
            }
        }

        return new SourceScanResult
        {
            Publications = DeduplicateScan(publications),
            SourceStatus = new PublicationFreshnessSourceStatus
            {
                SourceName = "Academic Online",
                WasAttempted = true,
                IsAvailable = errors.Count < authorIds.Count,
                PagesFetched = pagesFetched,
                HitMaxPages = hitMaxPages,
                Message = BuildSourceStatusMessage(
                    errors,
                    hitMaxPages
                        ? "Academic Online reached its configured publication-page safeguard."
                        : null)
            }
        };
    }

    private async Task<SourceScanResult> ScanOpenAlexAsync(
        IReadOnlyList<string> authorIds,
        int fromYear,
        int toYear,
        CancellationToken cancellationToken)
    {
        if (authorIds.Count == 0)
        {
            return new SourceScanResult
            {
                SourceStatus = new PublicationFreshnessSourceStatus
                {
                    SourceName = "OpenAlex",
                    WasAttempted = false,
                    IsAvailable = false,
                    Message = "No trusted OpenAlex author identity was available."
                }
            };
        }

        var publications = new List<ScannedPublication>();
        var errors = new List<string>();
        var pagesFetched = 0;
        var hitMaxPages = false;

        foreach (var authorId in authorIds)
        {
            try
            {
                var cursor = "*";
                var authorPages = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    var filter =
                        $"authorships.author.id:{authorId}," +
                        $"from_publication_date:{fromYear:D4}-01-01," +
                        $"to_publication_date:{toYear:D4}-12-31";

                    using var page =
                        await _openAlexClient.GetWorksPageAsync(
                            filter,
                            cursor,
                            cancellationToken);

                    authorPages++;
                    pagesFetched++;

                    foreach (var work in page.ResultsElement.EnumerateArray())
                    {
                        publications.Add(
                            new ScannedPublication
                            {
                                Title =
                                    GetString(work, "title") ??
                                    GetString(work, "display_name") ??
                                    string.Empty,
                                Venue =
                                    GetNestedString(
                                        work,
                                        "primary_location",
                                        "source",
                                        "display_name"),
                                PublicationYear =
                                    GetInt(work, "publication_year"),
                                Doi =
                                    GetString(work, "doi"),
                                OpenAlexWorkId =
                                    GetString(work, "id"),
                                Link =
                                    GetNestedString(
                                        work,
                                        "primary_location",
                                        "landing_page_url") ??
                                    GetString(work, "doi") ??
                                    GetString(work, "id")
                            });
                    }

                    var nextCursor = page.NextCursor;

                    if (authorPages >= MaxOpenAlexPages)
                    {
                        hitMaxPages =
                            !string.IsNullOrWhiteSpace(nextCursor) &&
                            !string.Equals(
                                nextCursor,
                                cursor,
                                StringComparison.Ordinal);
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(nextCursor) ||
                        string.Equals(
                            nextCursor,
                            cursor,
                            StringComparison.Ordinal))
                    {
                        break;
                    }

                    cursor = nextCursor;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(
                    $"{authorId}: {ex.Message}");
            }
        }

        var messageParts = new List<string>();

        if (errors.Count > 0)
        {
            messageParts.Add(string.Join(" | ", errors));
        }

        if (hitMaxPages)
        {
            messageParts.Add(
                $"The OpenAlex pagination safeguard ({MaxOpenAlexPages} pages per author) was reached.");
        }

        return new SourceScanResult
        {
            Publications = DeduplicateScan(publications),
            SourceStatus = new PublicationFreshnessSourceStatus
            {
                SourceName = "OpenAlex",
                WasAttempted = true,
                IsAvailable = errors.Count < authorIds.Count,
                PagesFetched = pagesFetched,
                HitMaxPages = hitMaxPages,
                Message = messageParts.Count == 0
                    ? null
                    : string.Join(" ", messageParts)
            }
        };
    }

    private static string? BuildSourceStatusMessage(
        IReadOnlyList<string> errors,
        string? additionalMessage)
    {
        var parts = new List<string>();

        if (errors.Count > 0)
        {
            parts.Add(string.Join(" | ", errors));
        }

        if (!string.IsNullOrWhiteSpace(additionalMessage))
        {
            parts.Add(additionalMessage);
        }

        return parts.Count == 0
            ? null
            : string.Join(" ", parts);
    }

    private static IReadOnlyList<ScannedPublication> DeduplicateScan(
        IEnumerable<ScannedPublication> source)
    {
        var accumulators =
            new List<PublicationAccumulator>();

        foreach (var item in source)
        {
            AddPublication(
                accumulators,
                item,
                PublicationObservation.ExternalDeduplicationOnly);
        }

        return accumulators
            .Select(item => item.ToScannedPublication())
            .ToArray();
    }

    private static void AddPublication(
        List<PublicationAccumulator> accumulators,
        ScannedPublication publication,
        PublicationObservation observation)
    {
        var keys = BuildKeys(publication);

        var matches = accumulators
            .Where(item => item.Keys.Overlaps(keys))
            .ToList();

        PublicationAccumulator target;

        if (matches.Count == 0)
        {
            target = new PublicationAccumulator();
            accumulators.Add(target);
        }
        else
        {
            target = matches[0];

            foreach (var duplicate in matches.Skip(1).ToArray())
            {
                target.MergeFrom(duplicate);
                accumulators.Remove(duplicate);
            }
        }

        target.Keys.UnionWith(keys);
        target.MergePublication(publication);

        switch (observation)
        {
            case PublicationObservation.Local:
                target.ExistsLocally = true;
                break;

            case PublicationObservation.AcademicOnline:
                target.ExistsInAcademicOnline = true;
                break;

            case PublicationObservation.OpenAlex:
                target.ExistsInOpenAlex = true;
                break;
        }
    }

    private static void AddReviewState(
        List<PublicationAccumulator> accumulators,
        FacultyPublicationFreshnessReviewState review)
    {
        var publication =
            new ScannedPublication
            {
                Title = review.Title,
                PublicationYear = review.PublicationYear,
                Doi = review.Doi
            };

        var keys = BuildKeys(publication);

        if (keys.Count == 0)
        {
            return;
        }

        var matches = accumulators
            .Where(item => item.Keys.Overlaps(keys))
            .ToList();

        PublicationAccumulator target;

        if (matches.Count == 0)
        {
            target = new PublicationAccumulator();
            accumulators.Add(target);
        }
        else
        {
            target = matches[0];

            foreach (var duplicate in matches.Skip(1).ToArray())
            {
                target.MergeFrom(duplicate);
                accumulators.Remove(duplicate);
            }
        }

        target.Keys.UnionWith(keys);
        target.MergePublication(publication);

        if (review.IsCurrentSource &&
            !string.IsNullOrWhiteSpace(review.ReviewStatus))
        {
            target.ReviewStatuses.Add(review.ReviewStatus);
        }

        if (!string.IsNullOrWhiteSpace(review.DecisionStatus))
        {
            target.ReviewStatuses.Add(review.DecisionStatus!);
        }
    }

    private static PublicationFreshnessItem ToResultItem(
        PublicationAccumulator item)
    {
        var status = DetermineStatus(item);

        return new PublicationFreshnessItem
        {
            Title = string.IsNullOrWhiteSpace(item.Title)
                ? "(Untitled publication)"
                : item.Title,
            Venue = item.Venue,
            PublicationYear = item.PublicationYear,
            Doi = NormalizeDoi(item.Doi),
            OpenAlexWorkId = NormalizeOpenAlexWorkId(item.OpenAlexWorkId),
            Link = item.Link,
            ExistsLocally = item.ExistsLocally,
            ExistsInAcademicOnline = item.ExistsInAcademicOnline,
            ExistsInOpenAlex = item.ExistsInOpenAlex,
            ReviewStatus = item.ReviewStatuses
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Status = status
        };
    }

    private static PublicationFreshnessStatus DetermineStatus(
        PublicationAccumulator item)
    {
        if (item.ExistsLocally)
        {
            return PublicationFreshnessStatus.Current;
        }

        if (item.ReviewStatuses.Any(value =>
                string.Equals(
                    value,
                    "Pending",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return PublicationFreshnessStatus.PendingReview;
        }

        if (item.ReviewStatuses.Any(value =>
                string.Equals(
                    value,
                    "Rejected",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return PublicationFreshnessStatus.PreviouslyRejected;
        }

        if (item.ReviewStatuses.Any(value =>
                string.Equals(
                    value,
                    "Accepted",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return PublicationFreshnessStatus.AcceptedButMissingLocally;
        }

        if (item.ExistsInAcademicOnline &&
            item.ExistsInOpenAlex)
        {
            return PublicationFreshnessStatus.MissingLocally;
        }

        if (item.ExistsInOpenAlex)
        {
            return PublicationFreshnessStatus.OpenAlexOnly;
        }

        if (item.ExistsInAcademicOnline)
        {
            return PublicationFreshnessStatus.AcademicOnlineOnly;
        }

        return PublicationFreshnessStatus.ReviewRecordOnly;
    }

    private static HashSet<string> BuildKeys(
        ScannedPublication publication)
    {
        var keys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var doi = NormalizeDoi(publication.Doi);
        if (doi is not null)
        {
            keys.Add("doi:" + doi);
        }

        var openAlexWorkId =
            NormalizeOpenAlexWorkId(publication.OpenAlexWorkId);

        if (openAlexWorkId is not null)
        {
            keys.Add("oa:" + openAlexWorkId);
        }

        var normalizedTitle =
            NormalizeTitle(publication.Title);

        if (publication.PublicationYear.HasValue &&
            normalizedTitle.Length >= 8)
        {
            keys.Add(
                $"ty:{publication.PublicationYear.Value}:{normalizedTitle}");
        }

        return keys;
    }


    private static bool IsUsableOpenAlexResolutionStatus(
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

    private static string? NormalizeDoi(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.StartsWith(
                "doi:",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..].Trim();
        }

        normalized = normalized
            .Replace(
                "https://doi.org/",
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "http://doi.org/",
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Trim()
            .TrimEnd('.', ',', ';');

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.ToLowerInvariant();
    }

    private static string? NormalizeOrcid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim()
            .Replace(
                "https://orcid.org/",
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "http://orcid.org/",
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Trim('/');

        if (normalized.Length != 19)
        {
            return null;
        }

        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];

            if (index is 4 or 9 or 14)
            {
                if (character != '-')
                {
                    return null;
                }

                continue;
            }

            if (!char.IsDigit(character) &&
                !(index == 18 &&
                  (character == 'X' || character == 'x')))
            {
                return null;
            }
        }

        return normalized.ToUpperInvariant();
    }

    private static string? NormalizeOpenAlexAuthorId(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim()
            .Replace(
                "https://openalex.org/",
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "http://openalex.org/",
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Trim('/');

        if (normalized.Length < 2 ||
            !normalized.StartsWith(
                "A",
                StringComparison.OrdinalIgnoreCase) ||
            normalized.Skip(1).Any(character => !char.IsDigit(character)))
        {
            return null;
        }

        return normalized.ToUpperInvariant();
    }

    private static string? NormalizeOpenAlexWorkId(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim()
            .Replace(
                "https://openalex.org/",
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "http://openalex.org/",
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Trim('/');

        if (normalized.Length < 2 ||
            !normalized.StartsWith(
                "W",
                StringComparison.OrdinalIgnoreCase) ||
            normalized.Skip(1).Any(character => !char.IsDigit(character)))
        {
            return null;
        }

        return normalized.ToUpperInvariant();
    }

    private static string? TryGetOpenAlexWorkIdFromLink(
        string? link)
    {
        if (string.IsNullOrWhiteSpace(link) ||
            !link.Contains(
                "openalex.org/",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var marker = link.LastIndexOf(
            "/W",
            StringComparison.OrdinalIgnoreCase);

        if (marker < 0)
        {
            return null;
        }

        var candidate = link[(marker + 1)..]
            .Split('?', '#', '/')[0];

        return NormalizeOpenAlexWorkId(candidate);
    }

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var lastWasSpace = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder
            .ToString()
            .Trim();
    }

    private static string? GetString(
        JsonElement element,
        string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
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
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.ToString(), out var parsed)
            ? parsed
            : null;
    }

    private static string? GetNestedString(
        JsonElement element,
        params string[] path)
    {
        var current = element;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        if (current.ValueKind is
            JsonValueKind.Null or
            JsonValueKind.Undefined)
        {
            return null;
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()
            : current.ToString();
    }

    private sealed class OpenAlexIdentityFallbackResult
    {
        public IReadOnlyList<string> OpenAlexAuthorIds { get; init; } =
            Array.Empty<string>();

        public IReadOnlyList<string> Warnings { get; init; } =
            Array.Empty<string>();
    }

    private sealed class SourceScanResult
    {
        public IReadOnlyList<ScannedPublication> Publications { get; init; } =
            Array.Empty<ScannedPublication>();

        public PublicationFreshnessSourceStatus SourceStatus { get; init; } =
            new();
    }

    private sealed class ScannedPublication
    {
        public string Title { get; init; } = string.Empty;
        public string? Venue { get; init; }
        public int? PublicationYear { get; init; }
        public string? Doi { get; init; }
        public string? OpenAlexWorkId { get; init; }
        public string? Link { get; init; }
    }

    private sealed class PublicationAccumulator
    {
        public HashSet<string> Keys { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public string Title { get; private set; } = string.Empty;
        public string? Venue { get; private set; }
        public int? PublicationYear { get; private set; }
        public string? Doi { get; private set; }
        public string? OpenAlexWorkId { get; private set; }
        public string? Link { get; private set; }

        public bool ExistsLocally { get; set; }
        public bool ExistsInAcademicOnline { get; set; }
        public bool ExistsInOpenAlex { get; set; }

        public HashSet<string> ReviewStatuses { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public void MergePublication(
            ScannedPublication publication)
        {
            if (string.IsNullOrWhiteSpace(Title) &&
                !string.IsNullOrWhiteSpace(publication.Title))
            {
                Title = publication.Title.Trim();
            }

            Venue ??= NullIfWhiteSpace(publication.Venue);
            PublicationYear ??= publication.PublicationYear;
            Doi ??= NullIfWhiteSpace(publication.Doi);
            OpenAlexWorkId ??= NullIfWhiteSpace(publication.OpenAlexWorkId);
            Link ??= NullIfWhiteSpace(publication.Link);
        }

        public void MergeFrom(
            PublicationAccumulator other)
        {
            Keys.UnionWith(other.Keys);

            MergePublication(
                other.ToScannedPublication());

            ExistsLocally |= other.ExistsLocally;
            ExistsInAcademicOnline |= other.ExistsInAcademicOnline;
            ExistsInOpenAlex |= other.ExistsInOpenAlex;
            ReviewStatuses.UnionWith(other.ReviewStatuses);
        }

        public ScannedPublication ToScannedPublication()
        {
            return new ScannedPublication
            {
                Title = Title,
                Venue = Venue,
                PublicationYear = PublicationYear,
                Doi = Doi,
                OpenAlexWorkId = OpenAlexWorkId,
                Link = Link
            };
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }

    private enum PublicationObservation
    {
        Local,
        AcademicOnline,
        OpenAlex,
        ExternalDeduplicationOnly
    }
}

public enum PublicationFreshnessStatus
{
    Current,
    PendingReview,
    PreviouslyRejected,
    AcceptedButMissingLocally,
    MissingLocally,
    OpenAlexOnly,
    AcademicOnlineOnly,
    ReviewRecordOnly
}

public sealed class PublicationFreshnessItem
{
    public string Title { get; init; } = string.Empty;
    public string? Venue { get; init; }
    public int? PublicationYear { get; init; }
    public string? Doi { get; init; }
    public string? OpenAlexWorkId { get; init; }
    public string? Link { get; init; }

    public bool ExistsLocally { get; init; }
    public bool ExistsInAcademicOnline { get; init; }
    public bool ExistsInOpenAlex { get; init; }

    public string? ReviewStatus { get; init; }
    public PublicationFreshnessStatus Status { get; init; }

    public bool NeedsAttention =>
        Status != PublicationFreshnessStatus.Current;

    public string StatusLabel =>
        Status switch
        {
            PublicationFreshnessStatus.Current =>
                ExistsInAcademicOnline && ExistsInOpenAlex
                    ? "Current — both sources"
                    : ExistsInAcademicOnline
                        ? "Current — Academic Online matched"
                        : ExistsInOpenAlex
                            ? "Current — OpenAlex matched"
                            : "Current — local only",
            PublicationFreshnessStatus.PendingReview => "Pending review",
            PublicationFreshnessStatus.PreviouslyRejected => "Previously rejected",
            PublicationFreshnessStatus.AcceptedButMissingLocally => "Accepted review / missing locally",
            PublicationFreshnessStatus.MissingLocally => "Missing locally",
            PublicationFreshnessStatus.OpenAlexOnly => "OpenAlex only",
            PublicationFreshnessStatus.AcademicOnlineOnly => "Academic Online only",
            PublicationFreshnessStatus.ReviewRecordOnly => "Review record only",
            _ => Status.ToString()
        };
}

public sealed class PublicationFreshnessSourceStatus
{
    public string SourceName { get; init; } = string.Empty;
    public bool WasAttempted { get; init; }
    public bool IsAvailable { get; init; }
    public int PagesFetched { get; init; }
    public bool HitMaxPages { get; init; }
    public string? Message { get; init; }
}

public sealed class FacultyPublicationFreshnessResult
{
    public string UvaUserId { get; init; } = string.Empty;
    public string FacultyName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public IReadOnlyList<string> Departments { get; init; } =
        Array.Empty<string>();

    public int FromYear { get; init; }
    public int ToYear { get; init; }
    public DateTime CheckedAtUtc { get; init; }

    public IReadOnlyList<string> AcademicOnlineAuthorIds { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<string> OpenAlexAuthorIds { get; init; } =
        Array.Empty<string>();

    public int LocalPublicationCount { get; init; }
    public int AcademicOnlinePublicationCount { get; init; }
    public int OpenAlexPublicationCount { get; init; }

    public PublicationFreshnessSourceStatus AcademicOnlineSource { get; init; } =
        new();

    public PublicationFreshnessSourceStatus OpenAlexSource { get; init; } =
        new();

    public IReadOnlyList<string> Warnings { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<PublicationFreshnessItem> Items { get; init; } =
        Array.Empty<PublicationFreshnessItem>();

    public int AttentionCount =>
        Items.Count(item => item.NeedsAttention);

    public int CurrentCount =>
        Items.Count(item => item.Status == PublicationFreshnessStatus.Current);
}

public sealed class PublicationDoiLookupResult
{
    public string Doi { get; init; } = string.Empty;
    public DateTime CheckedAtUtc { get; init; }
    public bool OpenAlexWasAttempted { get; init; }
    public bool OpenAlexIsAvailable { get; init; }
    public string? OpenAlexMessage { get; init; }
    public PublicationDoiWork? Publication { get; init; }
    public IReadOnlyList<PublicationDoiOpenAlexAuthor> OpenAlexAuthors { get; init; } =
        Array.Empty<PublicationDoiOpenAlexAuthor>();
    public IReadOnlyList<PublicationDoiFacultyCandidate> FacultyCandidates { get; init; } =
        Array.Empty<PublicationDoiFacultyCandidate>();
    public IReadOnlyList<string> Warnings { get; init; } =
        Array.Empty<string>();

    public bool HasAnyEvidence =>
        Publication is not null || FacultyCandidates.Count > 0;
}

public sealed class PublicationDoiWork
{
    public string Title { get; init; } = string.Empty;
    public string? Venue { get; init; }
    public int? PublicationYear { get; init; }
    public string? PublicationDate { get; init; }
    public string Doi { get; init; } = string.Empty;
    public string? OpenAlexWorkId { get; init; }
    public string? WorkType { get; init; }
    public string? Link { get; init; }
}

public sealed class PublicationDoiOpenAlexAuthor
{
    public string? OpenAlexAuthorId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? Orcid { get; init; }
    public IReadOnlyList<string> InstitutionNames { get; init; } =
        Array.Empty<string>();
    public bool HasUvaAffiliationHint { get; init; }
}

public sealed class PublicationDoiFacultyCandidate
{
    public string UvaUserId { get; init; } = string.Empty;
    public string FacultyName { get; init; } = string.Empty;
    public IReadOnlyList<string> Departments { get; init; } =
        Array.Empty<string>();
    public string AssociationBasis { get; init; } = string.Empty;
    public bool HasTrustedOpenAlexIdentity { get; init; }
    public long? ReviewDepartmentId { get; init; }
    public string? ReviewDepartmentName { get; init; }
    public bool ExistsLocally { get; init; }
    public bool ExistsInAcademicOnline { get; init; }
    public bool ExistsInOpenAlex { get; init; }
    public string? ReviewStatus { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public bool AcademicOnlineWasAttempted { get; init; }
    public bool AcademicOnlineIsAvailable { get; init; }
    public bool OpenAlexWasAttempted { get; init; }
    public bool OpenAlexIsAvailable { get; init; }
    public IReadOnlyList<string> ScanWarnings { get; init; } =
        Array.Empty<string>();
    public string? ScanError { get; init; }

    public bool CanSendToReview =>
        HasTrustedOpenAlexIdentity &&
        !ExistsLocally &&
        string.IsNullOrWhiteSpace(ReviewStatus) &&
        string.IsNullOrWhiteSpace(ScanError);
}
