using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
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
        var departmentName = _options.DepartmentName?.Trim();

        if (string.IsNullOrWhiteSpace(departmentName))
        {
            throw new InvalidOperationException(
                "AcademicOnlineDepartmentSync:DepartmentName is required.");
        }

        ValidateYearRange();

        var maxScholars = Math.Clamp(
            _options.MaxScholarsToProcess,
            1,
            5000);

        var runId = await _repository.StartRunAsync(
            departmentName,
            _options.FromYear,
            _options.ToYear,
            _options.VerifiedOnly,
            maxScholars,
            _options.StartedBy,
            _options.ApplicationVersion,
            cancellationToken);

        var scholarsProcessed = 0;
        var publicationRowsProcessed = 0;
        var scholarPagesFetched = 0;
        var publicationPagesFetched = 0;
        var publicationPaginationSafeguardHitCount = 0;

        try
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

            scholarPagesFetched = departmentScholars.PagesFetched;

            if (departmentScholars.Scholars.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Academic Online returned no scholars for department '{departmentName}'. " +
                    "Verify the exact Academic Online department name before retrying.");
            }

            var departmentId = await _repository.UpsertDepartmentAsync(
                runId,
                departmentName,
                cancellationToken);

            var scholars = departmentScholars.Scholars
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
                        _options.VerifiedOnly,
                        rowsPerPage: Math.Clamp(
                            _options.PublicationRowsPerPage,
                            1,
                            100),
                        maxPages: Math.Clamp(
                            _options.MaxPublicationPages,
                            1,
                            1000),
                        fromYear: _options.FromYear,
                        toYear: _options.ToYear,
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

                    publicationRowsProcessed++;
                }

                scholarsProcessed++;
            }

            var scholarsDiscovered = departmentScholars.Scholars.Count;

            var isCacheComplete =
                !departmentScholars.HitMaxPages &&
                scholarsProcessed == scholarsDiscovered &&
                publicationPaginationSafeguardHitCount == 0;

            var summary =
                $"Academic Online department sync completed. " +
                $"Department={departmentName}; " +
                $"ScholarsDiscovered={scholarsDiscovered}; " +
                $"ScholarsProcessed={scholarsProcessed}; " +
                $"MaxScholarsToProcess={maxScholars}; " +
                $"PublicationRowsProcessed={publicationRowsProcessed}; " +
                $"ScholarPagesFetched={scholarPagesFetched}; " +
                $"PublicationPagesFetched={publicationPagesFetched}; " +
                $"FromYear={_options.FromYear?.ToString() ?? "(all)"}; " +
                $"ToYear={_options.ToYear?.ToString() ?? "(current)"}; " +
                $"VerifiedOnly={_options.VerifiedOnly}; " +
                $"ScholarPaginationSafeguardHit={departmentScholars.HitMaxPages}; " +
                $"PublicationPaginationSafeguardHitCount={publicationPaginationSafeguardHitCount}; " +
                $"CacheComplete={isCacheComplete}.";

            await _repository.CompleteRunAsync(
                runId,
                scholarsDiscovered,
                scholarsProcessed,
                publicationRowsProcessed,
                scholarPagesFetched,
                publicationPagesFetched,
                departmentScholars.HitMaxPages,
                publicationPaginationSafeguardHitCount,
                isCacheComplete,
                summary,
                cancellationToken);

            await _logger.LogInfoAsync(
                "AcademicOnlineDepartmentSync",
                "RunCompleted",
                summary,
                cancellationToken: cancellationToken);
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

            throw;
        }
    }

    private void ValidateYearRange()
    {
        if (_options.FromYear.HasValue &&
            _options.ToYear.HasValue &&
            _options.FromYear > _options.ToYear)
        {
            throw new InvalidOperationException(
                "AcademicOnlineDepartmentSync:FromYear cannot be greater than ToYear.");
        }

        foreach (var year in new[] { _options.FromYear, _options.ToYear })
        {
            if (year.HasValue &&
                (year < 1800 || year > DateTime.UtcNow.Year + 1))
            {
                throw new InvalidOperationException(
                    "AcademicOnlineDepartmentSync contains a publication year outside the allowed range.");
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
