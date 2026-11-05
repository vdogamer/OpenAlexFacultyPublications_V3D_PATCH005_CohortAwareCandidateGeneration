using Microsoft.Extensions.Logging.Abstractions;
using OpenAlex.Data;
using OpenAlex.Web.Pages.Reports;
using OpenAlex.Web.Services;

namespace OpenAlex.Tests;

public sealed class FacultyDoiLookupTests
{
    [Theory]
    [InlineData(" 10.1234/ABC ", "10.1234/abc")]
    [InlineData("HTTPS://DOI.ORG/10.1234/ABC", "10.1234/abc")]
    [InlineData("http://dx.doi.org/10.1234/ABC", "10.1234/abc")]
    [InlineData("doi: 10.1234/ABC", "10.1234/abc")]
    [InlineData("https://doi.org/10.1234%2FABC", "10.1234/abc")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("N/A", null)]
    [InlineData("https://example.org/10.1234/abc", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("10.1234/", null)]
    [InlineData("10.1234/has space", null)]
    [InlineData("10.1234/ab\u0000cd", null)]
    public void NormalizesOnlyValidDoiIdentifiers(string? input, string? expected)
    {
        Assert.Equal(expected, FacultyDoiLookupService.NormalizeDoi(input));
    }

    [Fact]
    public async Task DeduplicatesAcrossSourcesAndSortsNewestFirst()
    {
        var repository = new StubRepository(
            new FacultyDoiSourceRow("Faculty One", "https://doi.org/10.1234/ABC", "<b>Older title</b>", 2023, "Synchronized publications"),
            new FacultyDoiSourceRow("Faculty One", "doi:10.1234/abc", "<b>Newest title</b>", 2024, "Accepted OpenAlex publications"),
            new FacultyDoiSourceRow("Faculty One", "10.1234/new", "New publication", 2026, "Synchronized publications"),
            new FacultyDoiSourceRow("Faculty One", null, "No DOI", 2026, "Synchronized publications"),
            new FacultyDoiSourceRow("Faculty One", "not a DOI", "Malformed DOI", 2026, "Synchronized publications"));
        var result = await new FacultyDoiLookupService(repository).LookupAsync(" CF2S@Virginia.edu ");

        Assert.Equal("cf2s", repository.LastUserId);
        Assert.Equal("cf2s", result.UserId);
        Assert.True(result.FacultyFound);
        Assert.Single(result.FacultyNames);
        Assert.Equal(new[] { "10.1234/new", "10.1234/abc" }, result.Dois.Select(entry => entry.Doi));
        var merged = result.Dois[1];
        Assert.Equal("Newest title", merged.Title);
        Assert.Equal(2024, merged.Year);
        Assert.Equal("https://doi.org/10.1234/abc", merged.Url);
        Assert.Equal(2, merged.Sources.Count);
    }

    [Fact]
    public async Task ReturnsAllDoisWithoutAnImplicitResultLimit()
    {
        var rows = Enumerable.Range(0, 6001).Select(index =>
            new FacultyDoiSourceRow("Faculty One", $"10.1234/work{index}", "Title", null, "Stored"));
        var result = await new FacultyDoiLookupService(new StubRepository(rows.ToArray())).LookupAsync("cf2s");
        Assert.Equal(6001, result.Dois.Count);
    }

    [Fact]
    public async Task EscapesDoiUrlSuffixWithoutTurningItIntoQueryOrFragment()
    {
        var repository = new StubRepository(new FacultyDoiSourceRow("Faculty One", "10.1234/a?x=1#part", "Title", null, "Stored"));
        var result = await new FacultyDoiLookupService(repository).LookupAsync("cf2s");
        Assert.Equal("https://doi.org/10.1234/a%3Fx%3D1%23part", Assert.Single(result.Dois).Url);
    }

    [Fact]
    public async Task DistinguishesUnknownFacultyFromFacultyWithNoDoi()
    {
        var unknown = await new FacultyDoiLookupService(new StubRepository()).LookupAsync("cf2s");
        var known = await new FacultyDoiLookupService(new StubRepository(
            new FacultyDoiSourceRow("Faculty One", null, null, null, "Stored"))).LookupAsync("cf2s");
        Assert.False(unknown.FacultyFound);
        Assert.True(known.FacultyFound);
        Assert.Empty(unknown.Dois);
        Assert.Empty(known.Dois);
    }

    [Fact]
    public async Task RosterMembershipRecognizesFacultyWithoutInventingPublications()
    {
        var result = await new FacultyDoiLookupService(new StubRepository(
            new FacultyDoiSourceRow("Roster Faculty", null, null, null, "Imported faculty roster", true)))
            .LookupAsync("cf2s");
        Assert.True(result.FacultyFound);
        Assert.True(result.InImportedRoster);
        Assert.Equal(new[] { "Roster Faculty" }, result.FacultyNames);
        Assert.Empty(result.Dois);
    }

    [Fact]
    public async Task UsesRosterNameWhilePreservingAndDeduplicatingPublicationResults()
    {
        var result = await new FacultyDoiLookupService(new StubRepository(
            new FacultyDoiSourceRow("Roster Faculty", null, null, null, "Imported faculty roster", true),
            new FacultyDoiSourceRow("Provider name", "10.1234/work", "Title", 2026, "Synchronized publications"),
            new FacultyDoiSourceRow("Native name", "https://doi.org/10.1234/WORK", "Title", 2026, "Accepted OpenAlex publications")))
            .LookupAsync("cf2s");
        Assert.Equal(new[] { "Roster Faculty" }, result.FacultyNames);
        var doi = Assert.Single(result.Dois);
        Assert.Equal(2, doi.Sources.Count);
        Assert.DoesNotContain("Imported faculty roster", doi.Sources);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("x")]
    [InlineData("cf2s@example.org")]
    [InlineData("cf2s' OR 1=1--")]
    [InlineData("cf2s cf3s")]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234567")]
    public async Task RejectsInvalidUserIdsBeforeQuerying(string userId)
    {
        var repository = new StubRepository();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FacultyDoiLookupService(repository).LookupAsync(userId));
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task InitialPageDoesNotQueryTheDatabase()
    {
        var repository = new StubRepository();
        var page = MakePage(repository);
        await page.OnGetAsync(CancellationToken.None);
        Assert.Null(page.Result);
        Assert.Null(page.ErrorMessage);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task InvalidSubmittedUserIdShowsValidationMessage()
    {
        var repository = new StubRepository();
        var page = MakePage(repository);
        page.UserId = " ";
        await page.OnGetAsync(CancellationToken.None);
        Assert.Contains("Enter a valid userid", page.ErrorMessage);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task PageLoadsNormalizedUserIdAndResults()
    {
        var repository = new StubRepository(new FacultyDoiSourceRow("Faculty One", "10.1234/test", "Title", 2026, "Stored"));
        var page = MakePage(repository);
        page.UserId = " CF2S ";
        await page.OnGetAsync(CancellationToken.None);
        Assert.Equal("cf2s", page.UserId);
        Assert.Single(page.Result!.Dois);
        Assert.Null(page.ErrorMessage);
    }

    [Fact]
    public async Task DatabaseFailuresDoNotAppearAsAnEmptyResultOrExposeExceptionDetails()
    {
        var page = MakePage(new StubRepository { Error = new InvalidOperationException("Private SQL connection details") });
        page.UserId = "cf2s";
        await page.OnGetAsync(CancellationToken.None);
        Assert.Null(page.Result);
        Assert.Equal("Publication data could not be loaded. Please try again or contact the application administrator.", page.ErrorMessage);
    }

    [Fact]
    public async Task RequestCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var page = MakePage(new StubRepository { Error = new OperationCanceledException(cancellation.Token) });
        page.UserId = "cf2s";
        await Assert.ThrowsAsync<OperationCanceledException>(() => page.OnGetAsync(cancellation.Token));
    }

    private static FacultyDoisModel MakePage(StubRepository repository) =>
        new(new FacultyDoiLookupService(repository), NullLogger<FacultyDoisModel>.Instance);

    private sealed class StubRepository : IFacultyDoiRepository
    {
        private readonly IReadOnlyList<FacultyDoiSourceRow> _rows;
        public StubRepository(params FacultyDoiSourceRow[] rows) => _rows = rows;
        public string? LastUserId { get; private set; }
        public int Calls { get; private set; }
        public Exception? Error { get; init; }

        public Task<IReadOnlyList<FacultyDoiSourceRow>> GetByUserIdAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastUserId = userId;
            if (Error is not null) throw Error;
            return Task.FromResult(_rows);
        }
    }
}
