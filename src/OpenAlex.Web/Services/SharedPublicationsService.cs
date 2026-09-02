using OpenAlex.OpenAlexClient;

namespace OpenAlex.Web.Services;

public sealed class SharedPublicationsService
{
    private const int MaxUserIds = 25;

    private readonly AcademicOnlineUniversalFacultyLookupService _lookupService;

    public SharedPublicationsService(
        AcademicOnlineUniversalFacultyLookupService lookupService)
    {
        _lookupService = lookupService;
    }

    public async Task<SharedPublicationsSearchResult> SearchAsync(
        IEnumerable<string> rawUserIds,
        bool verifiedOnly,
        CancellationToken cancellationToken = default)
    {
        var userIds = rawUserIds
            .Select(AcademicOnlineUniversalFacultyLookupService.NormalizeUvaUserId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (userIds.Count < 2)
        {
            throw new InvalidOperationException(
                "Enter at least two distinct UVA User IDs.");
        }

        if (userIds.Count > MaxUserIds)
        {
            throw new InvalidOperationException(
                $"A maximum of {MaxUserIds} UVA User IDs can be searched at once.");
        }

        var resolutions = new List<SharedFacultyResolution>();
        var resolvedPublicationSets = new List<ResolvedPublicationSet>();

        foreach (var userId in userIds)
        {
            var candidates = await _lookupService.SearchByUvaUserIdAsync(
                userId,
                cancellationToken);

            if (candidates.Count == 0)
            {
                resolutions.Add(new SharedFacultyResolution
                {
                    UserId = userId,
                    Status = "NotFound",
                    Message =
                        $"No Academic Online scholar with exact email {userId}@virginia.edu was found."
                });
                continue;
            }

            if (candidates.Count > 1)
            {
                resolutions.Add(new SharedFacultyResolution
                {
                    UserId = userId,
                    Status = "Ambiguous",
                    CandidateCount = candidates.Count,
                    Message =
                        $"Academic Online returned {candidates.Count} scholar profiles with the exact UVA email. Resolve the duplicate profiles before using them in a shared-publication search."
                });
                continue;
            }

            var candidate = candidates[0];

            var publicationResult = await _lookupService.GetPublicationsAsync(
                candidate.AuthorId,
                verifiedOnly,
                fromYear: null,
                toYear: null,
                cancellationToken: cancellationToken);

            var profile = publicationResult.RenderResult.Profile;
            var publications = publicationResult.RenderResult.Publications;

            resolutions.Add(new SharedFacultyResolution
            {
                UserId = userId,
                Status = "Resolved",
                Name = profile.Name,
                Email = profile.Email ?? candidate.Email,
                Department = profile.Department ?? candidate.Department,
                AcademicOnlineAuthorId = candidate.AuthorId,
                PublicationCount = publications.Count
            });

            resolvedPublicationSets.Add(new ResolvedPublicationSet(
                userId,
                publications,
                PublicationIndex.Build(publications)));
        }

        if (resolutions.Any(item =>
                !string.Equals(
                    item.Status,
                    "Resolved",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return new SharedPublicationsSearchResult
            {
                VerifiedOnly = verifiedOnly,
                Resolutions = resolutions,
                Publications = Array.Empty<SharedPublicationMatch>()
            };
        }

        var publicationsSharedByAll =
            FindIntersection(resolvedPublicationSets);

        return new SharedPublicationsSearchResult
        {
            VerifiedOnly = verifiedOnly,
            Resolutions = resolutions,
            Publications = publicationsSharedByAll
        };
    }

    private static IReadOnlyList<SharedPublicationMatch> FindIntersection(
        IReadOnlyList<ResolvedPublicationSet> sets)
    {
        if (sets.Count == 0)
        {
            return Array.Empty<SharedPublicationMatch>();
        }

        // Use the faculty member with the fewest publications as the seed so the
        // intersection requires the least amount of comparison work.
        var seed = sets
            .OrderBy(item => item.Publications.Count)
            .First();

        var otherSets = sets
            .Where(item =>
                !string.Equals(
                    item.UserId,
                    seed.UserId,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        var matches = new List<SharedPublicationMatch>();
        var seenResults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var publication in seed.Publications)
        {
            var weakestMatchRank = 4;
            var matchedAll = true;

            foreach (var other in otherSets)
            {
                var match = other.Index.TryFind(publication);
                if (match is null)
                {
                    matchedAll = false;
                    break;
                }

                weakestMatchRank = Math.Min(
                    weakestMatchRank,
                    MatchBasisRank(match.MatchBasis));
            }

            if (!matchedAll)
            {
                continue;
            }

            var resultKey = BuildResultKey(publication);
            if (!seenResults.Add(resultKey))
            {
                continue;
            }

            matches.Add(new SharedPublicationMatch
            {
                Title = publication.Title ?? "(Untitled publication)",
                Venue = publication.Venue,
                PublicationYear = publication.PublicationYear,
                CitationCount = publication.CitationCount,
                Doi = publication.Doi,
                OpenAlexId = publication.OpenAlexId,
                AcademicOnlinePublicationId = publication.Id,
                Link = publication.Link,
                Verified = publication.Verified,
                SharedFacultyCount = sets.Count,
                MatchBasis = MatchBasisFromRank(weakestMatchRank)
            });
        }

        return matches
            .OrderByDescending(item => item.PublicationYear ?? int.MinValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int MatchBasisRank(string matchBasis)
    {
        return matchBasis switch
        {
            "OpenAlex ID" => 4,
            "DOI" => 3,
            "Academic Online publication ID" => 2,
            _ => 1
        };
    }

    private static string MatchBasisFromRank(int rank)
    {
        return rank switch
        {
            >= 4 => "OpenAlex ID",
            3 => "DOI",
            2 => "Academic Online publication ID",
            _ => "Title + year fallback"
        };
    }

    private static string BuildResultKey(
        AcademicOnlinePublicationItem publication)
    {
        var openAlexId = NormalizeOpenAlexId(publication.OpenAlexId);
        if (openAlexId is not null)
        {
            return "openalex:" + openAlexId;
        }

        var doi = NormalizeDoi(publication.Doi);
        if (doi is not null)
        {
            return "doi:" + doi;
        }

        if (!string.IsNullOrWhiteSpace(publication.Id))
        {
            return "academic-online:" + publication.Id.Trim().ToLowerInvariant();
        }

        return "title-year:" +
               NormalizeTitle(publication.Title) +
               "|" +
               (publication.PublicationYear?.ToString() ??
                publication.YearText ??
                string.Empty);
    }

    private static string? NormalizeOpenAlexId(string? value)
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

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.ToUpperInvariant();
    }

    private static string? NormalizeDoi(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim()
            .Replace(
                "https://doi.org/",
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "http://doi.org/",
                string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "http://dx.doi.org/",
                string.Empty,
                StringComparison.OrdinalIgnoreCase);

        if (normalized.StartsWith(
                "doi:",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..].Trim();
        }

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.ToLowerInvariant();
    }

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var characters = value
            .Trim()
            .ToLowerInvariant()
            .Select(character =>
                char.IsLetterOrDigit(character) ||
                char.IsWhiteSpace(character)
                    ? character
                    : ' ')
            .ToArray();

        return string.Join(
            " ",
            new string(characters)
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries));
    }

    private sealed record ResolvedPublicationSet(
        string UserId,
        IReadOnlyList<AcademicOnlinePublicationItem> Publications,
        PublicationIndex Index);

    private sealed class PublicationIndex
    {
        private readonly Dictionary<string, AcademicOnlinePublicationItem> _openAlex =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AcademicOnlinePublicationItem> _doi =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AcademicOnlinePublicationItem> _academicOnline =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AcademicOnlinePublicationItem> _titleYear =
            new(StringComparer.OrdinalIgnoreCase);

        public static PublicationIndex Build(
            IReadOnlyList<AcademicOnlinePublicationItem> publications)
        {
            var index = new PublicationIndex();

            foreach (var publication in publications)
            {
                var openAlexId = NormalizeOpenAlexId(publication.OpenAlexId);
                if (openAlexId is not null)
                {
                    index._openAlex.TryAdd(openAlexId, publication);
                }

                var doi = NormalizeDoi(publication.Doi);
                if (doi is not null)
                {
                    index._doi.TryAdd(doi, publication);
                }

                if (!string.IsNullOrWhiteSpace(publication.Id))
                {
                    index._academicOnline.TryAdd(
                        publication.Id.Trim(),
                        publication);
                }

                var title = NormalizeTitle(publication.Title);
                var year =
                    publication.PublicationYear?.ToString() ??
                    publication.YearText;

                if (!string.IsNullOrWhiteSpace(title) &&
                    !string.IsNullOrWhiteSpace(year))
                {
                    index._titleYear.TryAdd(
                        title + "|" + year.Trim(),
                        publication);
                }
            }

            return index;
        }

        public PublicationLookupMatch? TryFind(
            AcademicOnlinePublicationItem publication)
        {
            var openAlexId = NormalizeOpenAlexId(publication.OpenAlexId);
            if (openAlexId is not null &&
                _openAlex.TryGetValue(openAlexId, out var byOpenAlex))
            {
                return new PublicationLookupMatch(
                    byOpenAlex,
                    "OpenAlex ID");
            }

            var doi = NormalizeDoi(publication.Doi);
            if (doi is not null &&
                _doi.TryGetValue(doi, out var byDoi))
            {
                return new PublicationLookupMatch(
                    byDoi,
                    "DOI");
            }

            if (!string.IsNullOrWhiteSpace(publication.Id) &&
                _academicOnline.TryGetValue(
                    publication.Id.Trim(),
                    out var byAcademicOnline))
            {
                return new PublicationLookupMatch(
                    byAcademicOnline,
                    "Academic Online publication ID");
            }

            var title = NormalizeTitle(publication.Title);
            var year =
                publication.PublicationYear?.ToString() ??
                publication.YearText;

            if (!string.IsNullOrWhiteSpace(title) &&
                !string.IsNullOrWhiteSpace(year) &&
                _titleYear.TryGetValue(
                    title + "|" + year.Trim(),
                    out var byTitleYear))
            {
                return new PublicationLookupMatch(
                    byTitleYear,
                    "Title + year fallback");
            }

            return null;
        }
    }

    private sealed record PublicationLookupMatch(
        AcademicOnlinePublicationItem Publication,
        string MatchBasis);
}

public sealed class SharedPublicationsSearchResult
{
    public bool VerifiedOnly { get; init; }

    public IReadOnlyList<SharedFacultyResolution> Resolutions { get; init; } =
        Array.Empty<SharedFacultyResolution>();

    public IReadOnlyList<SharedPublicationMatch> Publications { get; init; } =
        Array.Empty<SharedPublicationMatch>();

    public bool HasResolutionErrors =>
        Resolutions.Any(item =>
            !string.Equals(
                item.Status,
                "Resolved",
                StringComparison.OrdinalIgnoreCase));
}

public sealed class SharedFacultyResolution
{
    public string UserId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? Email { get; init; }
    public string? Department { get; init; }
    public string? AcademicOnlineAuthorId { get; init; }
    public int PublicationCount { get; init; }
    public int CandidateCount { get; init; }
    public string? Message { get; init; }
}

public sealed class SharedPublicationMatch
{
    public string Title { get; init; } = string.Empty;
    public string? Venue { get; init; }
    public int? PublicationYear { get; init; }
    public int? CitationCount { get; init; }
    public string? Doi { get; init; }
    public string? OpenAlexId { get; init; }
    public string? AcademicOnlinePublicationId { get; init; }
    public string? Link { get; init; }
    public bool? Verified { get; init; }
    public int SharedFacultyCount { get; init; }
    public string MatchBasis { get; init; } = string.Empty;
}
