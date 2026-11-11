using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenAlex.Web.Services.PubMed;

namespace OpenAlex.Tests;

public sealed class PubMedClientTests
{
    internal const string FixtureXml = """
        <?xml version="1.0"?>
        <!DOCTYPE PubmedArticleSet SYSTEM "https://example.invalid/pubmed.dtd">
        <PubmedArticleSet>
          <PubmedArticle><MedlineCitation><PMID>101</PMID><Article>
            <ArticleTitle>Title &lt;script&gt;literal&lt;/script&gt; with <i>markup</i></ArticleTitle>
            <Journal><Title>Fixture journal</Title><JournalIssue><PubDate><MedlineDate>2001 Winter</MedlineDate></PubDate></JournalIssue></Journal>
            <AuthorList><Author><LastName>Example</LastName><ForeName>Alex</ForeName><Initials>A</Initials>
              <Identifier Source="ORCID">https://orcid.org/0000-0002-1825-0097</Identifier>
              <AffiliationInfo><Affiliation>Previous University, Example Department</Affiliation></AffiliationInfo>
            </Author><Author><LastName>Other</LastName><ForeName>Casey</ForeName><Initials>C</Initials>
              <AffiliationInfo><Affiliation>University of Virginia</Affiliation></AffiliationInfo>
            </Author><Author><CollectiveName>Fixture Research Group</CollectiveName></Author></AuthorList>
            <PublicationTypeList><PublicationType>Journal Article</PublicationType></PublicationTypeList>
          </Article><InvestigatorList><Investigator><LastName>Member</LastName><ForeName>Sam</ForeName><Initials>S</Initials>
            <Identifier Source="ORCID">0000-0002-1825-0097</Identifier>
          </Investigator></InvestigatorList></MedlineCitation><PubmedData><ArticleIdList>
            <ArticleId IdType="pubmed">101</ArticleId><ArticleId IdType="pmc">PMC201</ArticleId>
            <ArticleId IdType="doi">https://doi.org/10.1234/TEST</ArticleId>
          </ArticleIdList></PubmedData></PubmedArticle>
          <PubmedArticle><MedlineCitation><PMID>102</PMID><Article><ArticleTitle>No DOI paper</ArticleTitle>
            <AuthorList><Author><LastName>Example</LastName><Initials>A</Initials></Author></AuthorList>
          </Article></MedlineCitation><PubmedData><ArticleIdList><ArticleId IdType="pubmed">102</ArticleId></ArticleIdList></PubmedData></PubmedArticle>
        </PubmedArticleSet>
        """;

    [Fact]
    public void XmlRetainsPmidOnlyWorksAndAuthorSpecificAffiliationsAndRoles()
    {
        var articles = PubMedXmlParser.Parse(FixtureXml);
        Assert.Equal(2,articles.Count);
        var first = articles[0];
        Assert.Equal("101",first.Pmid);
        Assert.Equal("PMC201",first.Pmcid);
        Assert.Equal("10.1234/test",first.Doi);
        Assert.Equal(2001,first.Year);
        Assert.Equal("Title <script>literal</script> with markup",first.Title);
        Assert.Equal("Previous University, Example Department",Assert.Single(first.Contributors[0].Affiliations));
        Assert.Equal("University of Virginia",Assert.Single(first.Contributors[1].Affiliations));
        Assert.Equal("Group author",first.Contributors[2].Role);
        Assert.Equal("Investigator/collaborator",first.Contributors[3].Role);
        Assert.Equal("0000-0002-1825-0097",Assert.Single(first.Contributors[0].Orcids));
        Assert.Null(articles[1].Doi);
        Assert.Null(articles[1].Pmcid);
        Assert.Null(articles[1].Year);
        Assert.Empty(Assert.Single(articles[1].Contributors).Affiliations);
    }

    [Fact]
    public void BookCitationsKeepTheirPmidsAndIdentifyEditors()
    {
        var article = Assert.Single(PubMedXmlParser.Parse("""
            <PubmedArticleSet><PubmedBookArticle><BookDocument><PMID>103</PMID>
            <ArticleTitle>Fixture chapter</ArticleTitle><Book><BookTitle>Fixture book</BookTitle><PubDate><Year>1999</Year></PubDate></Book>
            <AuthorList Type="editors"><Author><LastName>Editor</LastName><Initials>A</Initials></Author></AuthorList>
            </BookDocument><PubmedBookData><ArticleIdList><ArticleId IdType="doi">10.1234/book</ArticleId></ArticleIdList></PubmedBookData>
            </PubmedBookArticle></PubmedArticleSet>
            """));
        Assert.Equal("103",article.Pmid);
        Assert.Equal(1999,article.Year);
        Assert.Equal("Editor",Assert.Single(article.Contributors).Role);
    }

    [Fact]
    public void XmlDoesNotExpandExternalEntities()
    {
        Assert.Throws<System.Xml.XmlException>(() => PubMedXmlParser.Parse("""
            <!DOCTYPE PubmedArticleSet [<!ENTITY test SYSTEM "file:///not-a-real-local-file">]>
            <PubmedArticleSet><PubmedArticle><MedlineCitation><PMID>101</PMID><Article><ArticleTitle>&test;</ArticleTitle>
            </Article></MedlineCitation></PubmedArticle></PubmedArticleSet>
            """));
    }

    [Fact]
    public async Task ClientUsesFixedEndpointsBoundedGetRequestsAndReturnsRequestedOrder()
    {
        var handler = new Handler(
            """{"esearchresult":{"count":"900","idlist":["102","101"],"querytranslation":"translated query"}}""",
            FixtureXml);
        using var http = new HttpClient(handler);
        using var gate = new PubMedRequestGate(TimeSpan.Zero);
        var client = Client(http,gate);
        var response = await client.SearchAsync(PubMedQueryBuilder.Build(new(PubMedSearchMode.Author,"Example A",Limit:10)));
        Assert.Equal(900,response.TotalFound);
        Assert.Equal(new[] {"102","101"},response.Articles.Select(article => article.Pmid));
        Assert.Equal(2,handler.Requests.Count);
        Assert.All(handler.Requests,uri => Assert.Equal("eutils.ncbi.nlm.nih.gov",uri.Host));
        Assert.Contains("retmax=10",handler.Requests[0].Query);
        Assert.Contains("db=pubmed",handler.Requests[0].Query);
        Assert.Contains("tool=OpenAlexFacultyPublications",handler.Requests[0].Query);
        Assert.Contains("email=maintainer%40example.org",handler.Requests[0].Query);
        Assert.Contains("id=102%2C101",handler.Requests[1].Query);
        Assert.DoesNotContain("UserId",string.Join("",handler.Requests.Select(uri => uri.Query)));
    }

    [Fact]
    public async Task EmptySearchDoesNotFetchAndRetainsPubMedNotices()
    {
        var handler = new Handler("""{"esearchresult":{"count":"0","idlist":[],"warninglist":{"outputmessages":["No items found."]}}}""");
        using var http = new HttpClient(handler);
        using var gate = new PubMedRequestGate(TimeSpan.Zero);
        var response = await Client(http,gate).SearchAsync(PubMedQueryBuilder.Build(new(PubMedSearchMode.Identifiers,Identifiers:"PMC201")));
        Assert.Empty(response.Articles);
        Assert.Single(handler.Requests);
        Assert.Single(response.Notices);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("[]")]
    [InlineData("{\"esearchresult\":[]}")]
    [InlineData("{\"error\":\"provider failure\"}")]
    [InlineData("{\"esearchresult\":{\"count\":\"4\",\"idlist\":[]}}")]
    [InlineData("{\"esearchresult\":{\"count\":\"4\",\"idlist\":[\"not-an-id\"]}}")]
    [InlineData("{\"esearchresult\":{\"count\":\"0\",\"idlist\":[\"101\"]}}")]
    [InlineData("{\"esearchresult\":{\"count\":\"1\",\"idlist\":[101]}}")]
    public async Task InvalidProviderResponsesAreFailuresRatherThanEmptyResults(string json)
    {
        using var http = new HttpClient(new Handler(json));
        using var gate = new PubMedRequestGate(TimeSpan.Zero);
        await Assert.ThrowsAsync<PubMedProviderException>(() => Client(http,gate).SearchAsync(
            PubMedQueryBuilder.Build(new(PubMedSearchMode.Author,"Example A"))));
    }

    [Fact]
    public async Task MissingFetchedRecordsAreExplicitlyIncomplete()
    {
        var handler = new Handler("""{"esearchresult":{"count":"2","idlist":["101","999"]}}""",FixtureXml);
        using var http = new HttpClient(handler);
        using var gate = new PubMedRequestGate(TimeSpan.Zero);
        var response = await Client(http,gate).SearchAsync(PubMedQueryBuilder.Build(new(PubMedSearchMode.Identifiers,Identifiers:"101 999")));
        Assert.Equal("101",Assert.Single(response.Articles).Pmid);
        Assert.Contains(response.Notices,notice => notice.Contains("incomplete",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.Notices,notice => notice.Contains("Unexpected",StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(403)]
    [InlineData(503)]
    public async Task HttpFailuresAreNotShownAsNoPublications(int status)
    {
        using var http = new HttpClient(new Handler("private upstream body") { Status=(HttpStatusCode)status });
        using var gate = new PubMedRequestGate(TimeSpan.Zero);
        var error = await Assert.ThrowsAsync<PubMedProviderException>(() => Client(http,gate).SearchAsync(
            PubMedQueryBuilder.Build(new(PubMedSearchMode.Author,"Example A"))));
        Assert.DoesNotContain("private upstream body",error.Message);
    }

    [Fact]
    public async Task RequestCancellationPropagatesWithoutCallingProvider()
    {
        var handler = new Handler("unused");
        using var http = new HttpClient(handler);
        using var gate = new PubMedRequestGate(TimeSpan.Zero);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(http,gate).SearchAsync(
            PubMedQueryBuilder.Build(new(PubMedSearchMode.Author,"Example A")),cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MissingContactConfigurationIsAnExplicitFailureBeforeAnyRequest()
    {
        var handler = new Handler("unused");
        using var http = new HttpClient(handler);
        using var gate = new PubMedRequestGate(TimeSpan.Zero);
        var client = new PubMedClient(http,gate,Options.Create(new PubMedOptions()),new ConfigurationBuilder().Build());
        var error = await Assert.ThrowsAsync<PubMedProviderException>(() => client.SearchAsync(
            PubMedQueryBuilder.Build(new(PubMedSearchMode.Author,"Example A"))));
        Assert.Contains("contact email",error.Message);
        Assert.Empty(handler.Requests);
    }

    private static PubMedClient Client(HttpClient http,PubMedRequestGate gate) => new(http,gate,
        Options.Create(new PubMedOptions()),new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string,string?> { ["OpenAlex:MailTo"]="maintainer@example.org" }).Build());

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;
        public Handler(params string[] responses) => _responses = new(responses);
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public List<Uri> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Get,request.Method);
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(Status)
                { Content=new StringContent(_responses.Dequeue(),Encoding.UTF8) });
        }
    }
}
