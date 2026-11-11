using OpenAlex.Data;
using OpenAlex.Data.Models;
using OpenAlex.Web.Services.PubMed;

namespace OpenAlex.Tests;

public sealed class PubMedCandidateLookupTests
{
    [Fact]
    public void QueryUsesLiteralAuthorVariantsAndKeepsPastAffiliationsOptional()
    {
        var query = PubMedQueryBuilder.Build(new(PubMedSearchMode.Author,"Example A; Example AB; Example A"));
        Assert.Equal("(\"Example A\"[au] OR \"Example AB\"[au])",query.Term);
        Assert.DoesNotContain("Virginia",query.Term);
        Assert.Equal(2,query.AuthorTerms.Count);
        var filtered = PubMedQueryBuilder.Build(new(PubMedSearchMode.Author,"Example A",Affiliation:"Previous University",YearFrom:1990,YearTo:2010));
        Assert.Contains("\"Previous University\"[ad]",filtered.Term);
        Assert.Contains("1990:2010[dp]",filtered.Term);
    }

    [Fact]
    public void KnownIdentifierModeKeepsPmidsDistinctFromPmcidsAndDoesNotHideThemWithFilters()
    {
        var query = PubMedQueryBuilder.Build(new(PubMedSearchMode.Identifiers,Identifiers:"101, PMC101 101",
            Affiliation:"University of Virginia",YearFrom:2020,YearTo:2026));
        Assert.Equal("(101[pmid] OR PMC101)",query.Term);
        Assert.Equal(2,query.RequestedIdentifiers.Count);
        Assert.Null(query.Affiliation);
        Assert.DoesNotContain("[dp]",query.Term);
    }

    [Theory]
    [InlineData("0000-0002-1825-0097","0000-0002-1825-0097")]
    [InlineData("https://orcid.org/0000-0002-1825-0097","0000-0002-1825-0097")]
    [InlineData("0000-0002-1825-0098",null)]
    [InlineData("not an orcid",null)]
    public void OrcidChecksumAndFormatAreChecked(string input,string? expected) =>
        Assert.Equal(expected,PubMedQueryBuilder.NormalizeOrcid(input));

    [Fact]
    public void OrcidSearchUsesAuthorIdentifierField()
    {
        var query = PubMedQueryBuilder.Build(new(PubMedSearchMode.Orcid,Orcid:"0000-0002-1825-0097"));
        Assert.Equal("\"0000-0002-1825-0097\"[auid]",query.Term);
    }

    [Theory]
    [InlineData("Example A\" OR all[all]")]
    [InlineData("Example A[au]")]
    [InlineData("Example A; Example B; Example C; Example D; Example E; Example F")]
    [InlineData("")]
    public void SearchOperatorsAndUnboundedAuthorListsAreRejected(string author) =>
        Assert.Throws<PubMedInputException>(() => PubMedQueryBuilder.Build(new(PubMedSearchMode.Author,author)));

    [Fact]
    public void InvalidSearchBoundsAndIdentifiersAreRejected()
    {
        Assert.Throws<PubMedInputException>(() => PubMedQueryBuilder.Build(new(PubMedSearchMode.Author,"Example A",Limit:1000)));
        Assert.Throws<PubMedInputException>(() => PubMedQueryBuilder.Build(new(PubMedSearchMode.Author,"Example A",YearFrom:2026,YearTo:2020)));
        Assert.Throws<PubMedInputException>(() => PubMedQueryBuilder.Build(new(PubMedSearchMode.Identifiers,Identifiers:"PMC101.2")));
        Assert.Throws<PubMedInputException>(() => PubMedQueryBuilder.Build(new(PubMedSearchMode.Orcid,Orcid:"0000-0002-1825-0098")));
        Assert.Throws<PubMedInputException>(() => PubMedQueryBuilder.Build(new(PubMedSearchMode.Author,"Example A",Affiliation:"UVA\" OR all[all]")));
    }

    [Fact]
    public async Task FacultyWithoutAcademicOnlineDataCanBeResolvedFromDirectoryIdentityAlone()
    {
        var faculty = new FacultyStub(new FacultyListItem { ComputingId="demo01",DisplayName="Alex Example",
            IsRosterMember=true,DepartmentName="Fixture department" });
        var provider = new ProviderStub();
        var context = await new PubMedCandidateLookupService(faculty,provider).ResolveFacultyAsync(" DEMO01@Virginia.edu ");
        Assert.Equal("demo01",faculty.LastId);
        Assert.Equal("demo01",context!.UserId);
        Assert.Equal(new[] { "Alex Example" },context.Names);
        Assert.Equal(0,provider.Calls);
    }

    [Fact]
    public async Task UnknownAndInvalidFacultyDoNotTriggerPubMedSearch()
    {
        var faculty = new FacultyStub();
        var provider = new ProviderStub();
        var service = new PubMedCandidateLookupService(faculty,provider);
        Assert.Null(await service.ResolveFacultyAsync("unknown99"));
        Assert.Equal(1,faculty.Calls);
        await Assert.ThrowsAsync<PubMedInputException>(() => service.ResolveFacultyAsync("bad input!"));
        Assert.Equal(1,faculty.Calls);
        Assert.Equal(0,provider.Calls);
    }

    [Fact]
    public async Task ConflictingNativeIdentitiesMustBeReviewedBeforeSearching()
    {
        var faculty = new FacultyStub(
            new FacultyListItem { ComputingId="demo01",FacultyId=1,DisplayName="One" },
            new FacultyListItem { ComputingId="demo01",FacultyId=2,DisplayName="Two" });
        await Assert.ThrowsAsync<PubMedInputException>(() =>
            new PubMedCandidateLookupService(faculty,new ProviderStub()).ResolveFacultyAsync("demo01"));
    }

    [Fact]
    public async Task AffiliationEvidenceStaysWithItsContributorAndPmidOnlyRecordsRemainCandidates()
    {
        var provider = new ProviderStub { Articles=PubMedXmlParser.Parse(PubMedClientTests.FixtureXml) };
        var service = new PubMedCandidateLookupService(new FacultyStub(),provider);
        var context = new PubMedFacultyContext("demo01",new[] { "Alex Example" },Array.Empty<string>(),Array.Empty<string>());
        var query = PubMedQueryBuilder.Build(new(PubMedSearchMode.Author,"Example A",
            Orcid:"0000-0002-1825-0097",Affiliation:"University of Virginia"));
        var result = await service.SearchAsync(context,query);
        Assert.Equal(2,result.Candidates.Count);
        Assert.Null(result.Candidates[1].Article.Doi);
        var alex = Assert.Single(result.Candidates[0].ContributorEvidence.Where(row => row.Contributor.Name=="Alex Example"));
        Assert.True(alex.NameCompatible);
        Assert.True(alex.OrcidMatches);
        Assert.False(alex.AffiliationContainsFilter); // UVA belongs to a different contributor.
        var other = Assert.Single(result.Candidates[0].ContributorEvidence.Where(row => row.Contributor.Name=="Casey Other"));
        Assert.True(other.AffiliationContainsFilter);
        Assert.False(other.NameCompatible);
    }

    [Fact]
    public async Task MatchingOrcidEvidenceIsNotLostBehindAContributorDisplayLimit()
    {
        var authors = Enumerable.Range(1,30).Select(i => new PubMedContributor("Author","Other "+i,"Other","X",
            Array.Empty<string>(),Array.Empty<string>())).ToList();
        authors.Add(new PubMedContributor("Author","Target Person","Person","T",
            new[] { "0000-0002-1825-0097" },new[] { "Previous University" }));
        var provider = new ProviderStub { Articles=new[] { new PubMedArticle("101",null,null,"Fixture", "Journal",null,Array.Empty<string>(),authors) } };
        var service = new PubMedCandidateLookupService(new FacultyStub(),provider);
        var result = await service.SearchAsync(new("demo01",new[] { "Target Person" },Array.Empty<string>(),Array.Empty<string>()),
            PubMedQueryBuilder.Build(new(PubMedSearchMode.Orcid,Orcid:"0000-0002-1825-0097")));
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(31,candidate.TotalContributorCount);
        Assert.Equal(20,candidate.ContributorEvidence.Count);
        Assert.True(candidate.ContributorEvidence[0].OrcidMatches);
    }

    internal sealed class FacultyStub : IFacultyIdentityLookup
    {
        private readonly FacultyListItem[] _rows;
        public FacultyStub(params FacultyListItem[] rows) => _rows=rows;
        public int Calls { get; private set; }
        public string? LastId { get; private set; }
        public Task<IReadOnlyList<FacultyListItem>> GetByComputingIdAsync(string id,CancellationToken token=default)
        { token.ThrowIfCancellationRequested(); Calls++; LastId=id; return Task.FromResult<IReadOnlyList<FacultyListItem>>(_rows); }
    }

    internal sealed class ProviderStub : IPubMedClient
    {
        public int Calls { get; private set; }
        public Exception? Error { get; set; }
        public IReadOnlyList<PubMedArticle> Articles { get; set; } = Array.Empty<PubMedArticle>();
        public Task<PubMedSearchResponse> SearchAsync(PubMedQuery query,CancellationToken token=default)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            if (Error is not null) throw Error;
            return Task.FromResult(new PubMedSearchResponse(Articles.Count,Articles,query.Term,Array.Empty<string>(),DateTimeOffset.UtcNow));
        }
    }
}
