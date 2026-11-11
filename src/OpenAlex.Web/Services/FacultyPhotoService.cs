using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using OpenAlex.Data;

namespace OpenAlex.Web.Services;

public sealed class FacultyPhotoOptions
{
    // Public links checked against an official faculty profile. Keys are ComputingIDs.
    public Dictionary<string, FacultyPhotoLink> VerifiedLinks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FacultyPhotoLink
{
    public string PhotoUrl { get; set; } = string.Empty;
    public string ProfileUrl { get; set; } = string.Empty;
}

public enum FacultyPhotoStatus { Found, NotAvailable, FacultyNotFound, IdentityConflict, ProviderUnavailable }

public sealed record FacultyPhotoResult(FacultyPhotoStatus Status, string? PhotoUrl = null,
    string? ProfileUrl = null);

/// <summary>Checks an official photo only when its link is opened; never writes faculty data.</summary>
public sealed class FacultyPhotoService
{
    private const string PhotoBase = "https://apps.med.virginia.edu/ResearchFacultyDirectory/Exchange/Photos/Small/";
    private readonly IFacultyIdentityLookup _faculty;
    private readonly HttpClient _http;
    private readonly FacultyPhotoOptions _options;

    public FacultyPhotoService(IFacultyIdentityLookup faculty, HttpClient http, IOptions<FacultyPhotoOptions> options)
    { _faculty = faculty; _http = http; _options = options.Value; }

    public async Task<FacultyPhotoResult> LookupAsync(string userId, CancellationToken cancellationToken = default)
    {
        string id;
        try { id = AcademicOnlineUniversalFacultyLookupService.NormalizeUvaUserId(userId); }
        catch (InvalidOperationException ex)
        { throw new ArgumentException("Enter a valid UVA ComputingID or its @virginia.edu email address.", nameof(userId), ex); }
        var rows = await _faculty.GetByComputingIdAsync(id, cancellationToken);
        if (rows.Count == 0) return new(FacultyPhotoStatus.FacultyNotFound);
        if (rows.Where(row => row.FacultyId is not null).Select(row => row.FacultyId).Distinct().Count() > 1)
            return new(FacultyPhotoStatus.IdentityConflict);

        string? photoUrl = null;
        string? profileUrl = null;
        if (_options.VerifiedLinks.TryGetValue(id, out var verified)
            && IsOfficialUvaUrl(verified.PhotoUrl) && IsOfficialUvaUrl(verified.ProfileUrl))
        {
            photoUrl = verified.PhotoUrl;
            profileUrl = verified.ProfileUrl;
        }
        else
        {
            var personIds = rows.Where(row => row.IsRosterMember && row.DirectoryPersonId is > 0)
                .Select(row => row.DirectoryPersonId!.Value).Distinct().ToArray();
            if (personIds.Length > 1) return new(FacultyPhotoStatus.IdentityConflict);
            if (personIds.Length == 1)
                photoUrl = PhotoBase + id + "_" + personIds[0].ToString(CultureInfo.InvariantCulture) + ".jpg";
        }
        if (photoUrl is null) return new(FacultyPhotoStatus.NotAvailable);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, photoUrl);
            using var first = await _http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (first.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
            {
                using var get = new HttpRequestMessage(HttpMethod.Get, photoUrl);
                using var fallback = await _http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                return Classify(fallback, photoUrl, profileUrl);
            }
            return Classify(first, photoUrl, profileUrl);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(FacultyPhotoStatus.ProviderUnavailable, ProfileUrl: profileUrl); }
        catch (HttpRequestException)
        { return new(FacultyPhotoStatus.ProviderUnavailable, ProfileUrl: profileUrl); }
    }

    private static FacultyPhotoResult Classify(HttpResponseMessage response, string photoUrl, string? profileUrl)
    {
        if (response.IsSuccessStatusCode
            && response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            return new(FacultyPhotoStatus.Found, photoUrl, profileUrl);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone
            || response.IsSuccessStatusCode)
            return new(FacultyPhotoStatus.NotAvailable, ProfileUrl: profileUrl);
        return new(FacultyPhotoStatus.ProviderUnavailable, ProfileUrl: profileUrl);
    }

    public static bool IsOfficialUvaUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Fragment.Length == 0
        && (uri.Host.Equals("virginia.edu", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".virginia.edu", StringComparison.OrdinalIgnoreCase));
}
