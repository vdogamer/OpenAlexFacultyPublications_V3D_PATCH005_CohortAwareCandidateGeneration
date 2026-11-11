using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OpenAlex.Web.Services.PubMed;

public static class PubMedXmlParser
{
    public static IReadOnlyList<PubMedArticle> Parse(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersInDocument = 8_000_000
        };
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, settings);
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName != "PubmedArticleSet" || document.Descendants("ERROR").Any())
            throw new PubMedProviderException("PubMed returned records in an unexpected format.");

        var records = new List<PubMedArticle>();
        foreach (var item in document.Root.Elements())
        {
            var citation = item.Element("MedlineCitation") ?? item.Element("BookDocument");
            var article = citation?.Element("Article") ?? citation;
            var pmid = Value(citation?.Element("PMID"));
            if (!Regex.IsMatch(pmid, @"^[1-9][0-9]{0,11}$")) continue;
            var ids = (item.Element("PubmedData") ?? item.Element("PubmedBookData"))?
                .Element("ArticleIdList")?.Elements("ArticleId").ToArray() ?? Array.Empty<XElement>();
            var pmcid = ids.Where(id => IsAttribute(id, "IdType", "pmc"))
                .Select(id => NormalizePmcid(Value(id))).FirstOrDefault(id => id is not null);
            var doi = ids.Where(id => IsAttribute(id, "IdType", "doi"))
                .Concat(article?.Elements("ELocationID").Where(id => IsAttribute(id, "EIdType", "doi"))
                    ?? Enumerable.Empty<XElement>())
                .Select(id => FacultyDoiLookupService.NormalizeDoi(Value(id))).FirstOrDefault(id => id is not null);
            var title = Value(article?.Element("ArticleTitle"));
            if (title.Length == 0) title = Value(citation?.Element("Book")?.Element("BookTitle"));
            var journal = Value(article?.Element("Journal")?.Element("Title"));
            if (journal.Length == 0) journal = Value(article?.Element("Journal")?.Element("ISOAbbreviation"));
            if (journal.Length == 0) journal = Value(citation?.Element("Book")?.Element("Publisher")?.Element("PublisherName"));
            var pubDate = article?.Element("Journal")?.Element("JournalIssue")?.Element("PubDate")
                ?? citation?.Element("Book")?.Element("PubDate") ?? article?.Element("ArticleDate");
            var yearText = Value(pubDate?.Element("Year"));
            if (yearText.Length == 0)
                yearText = Regex.Match(Value(pubDate?.Element("MedlineDate")), @"\b[12][0-9]{3}\b").Value;
            int? year = int.TryParse(yearText, out var parsedYear) ? parsedYear : null;
            var types = article?.Element("PublicationTypeList")?.Elements("PublicationType")
                .Select(Value).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray()
                ?? Array.Empty<string>();
            var contributors = new List<PubMedContributor>();
            foreach (var list in article?.Elements("AuthorList") ?? Enumerable.Empty<XElement>())
                foreach (var author in list.Elements("Author"))
                    contributors.Add(ParseContributor(author, IsAttribute(list, "Type", "editors") ? "Editor" : "Author"));
            foreach (var investigator in citation?.Element("InvestigatorList")?.Elements("Investigator")
                         ?? Enumerable.Empty<XElement>())
                contributors.Add(ParseContributor(investigator, "Investigator/collaborator"));

            records.Add(new PubMedArticle(pmid, pmcid, doi,
                title.Length > 0 ? title : "(Title not supplied)",
                journal.Length > 0 ? journal : "(Journal not supplied)", year, types, contributors));
        }
        return records.GroupBy(article => article.Pmid, StringComparer.Ordinal).Select(group => group.First()).ToArray();
    }

    private static PubMedContributor ParseContributor(XElement element, string role)
    {
        var last = Value(element.Element("LastName"));
        var first = Value(element.Element("ForeName"));
        var initials = Value(element.Element("Initials"));
        var collective = Value(element.Element("CollectiveName"));
        var suffix = Value(element.Element("Suffix"));
        var name = collective.Length > 0 ? collective : string.Join(" ",
            new[] { first.Length > 0 ? first : initials, last, suffix }.Where(value => value.Length > 0));
        var orcids = element.Elements("Identifier").Where(id => IsAttribute(id, "Source", "ORCID"))
            .Select(id => PubMedQueryBuilder.NormalizeOrcid(Value(id))).Where(id => id is not null)
            .Select(id => id!).Distinct(StringComparer.Ordinal).ToArray();
        var affiliations = element.Elements("AffiliationInfo").Elements("Affiliation")
            .Select(Value).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        return new PubMedContributor(collective.Length > 0 ? "Group author" : role,
            name.Length > 0 ? name : "(Name not supplied)", last, initials, orcids, affiliations);
    }

    private static string? NormalizePmcid(string value)
    {
        var id = value.Trim().ToUpperInvariant();
        if (Regex.IsMatch(id, @"^[1-9][0-9]{0,11}$")) id = "PMC" + id;
        return Regex.IsMatch(id, @"^PMC[1-9][0-9]{0,11}$") ? id : null;
    }

    private static bool IsAttribute(XElement element, string name, string value) =>
        string.Equals(element.Attribute(name)?.Value, value, StringComparison.OrdinalIgnoreCase);

    private static string Value(XElement? element) =>
        Regex.Replace(element?.Value.Trim() ?? string.Empty, @"\s+", " ");
}
