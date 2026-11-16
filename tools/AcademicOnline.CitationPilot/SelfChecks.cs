using System.Data;
using System.Net;
using System.Text;
using System.Text.Json;

namespace AcademicOnline.CitationPilot;

internal static class SelfChecks
{
    private static int _passed;

    public static async Task<int> RunAsync()
    {
        try
        {
            await Check("ComputingID normalization and invalid input", () =>
            {
                Equal("demo01", DoiRules.UserId(" DEMO01@Virginia.edu "));
                Throws<FormatException>(() => DoiRules.UserId("bad input!"));
                Throws<FormatException>(() => DoiRules.UserId("demo01@external.example"));
            });
            await Check("DOI normalization retains raw text and separate review states", () =>
            {
                var raw = new string?[] { " HTTPS://doi.org/10.1234/PAPER ", "doi:10.1234/paper", "bad value", null, "10.1234/held" };
                var plan = DoiRules.Plan(Snapshot(raw), [new("DEMO01", "https://doi.org/10.1234/held", "Synthetic review hold", "https://example.org/review")]);
                Equal(5, plan.Length); Equal(raw[0], plan[0].RawDoi); Equal("10.1234/paper", plan[0].NormalizedDoi);
                Equal("MetadataNotRequested", plan[0].Status); Equal("DuplicateAssociation", plan[1].Status);
                Equal(1, plan[1].DuplicateOfSourceRow); Equal("InvalidDoi", plan[2].Status); Equal("InvalidDoi", plan[3].Status);
                Equal("Held", plan[4].Status); True(plan[4].ReviewReasons.Single().Contains("Synthetic review hold"));
                True(plan.All(row => !row.PreviewEligible));
            });
            await Check("Review holds are scoped to the selected ComputingID", () =>
            {
                var plan = DoiRules.Plan(Snapshot(["10.1234/paper"]), [new("other01", "10.1234/paper", "Other person's hold", null)]);
                Equal("MetadataNotRequested", plan[0].Status);
            });
            await Check("Incomplete snapshots cannot become citation exports", () =>
            {
                var read = Snapshot(["10.1234/paper"]);
                Throws<FormatException>(() => DoiRules.Plan(read with { Snapshot = read.Snapshot with { Complete = false } }, []));
                Throws<FormatException>(() => DoiRules.Plan(read with { Snapshot = read.Snapshot with { DoiRows = 2 } }, []));
            });
            await Check("SQL-record adapter preserves all stored DOI rows", async () =>
            {
                using var reader = Records(["10.1234/a", "10.1234/b"]);
                var result = await DatabaseReader.ParseAsync(reader, "demo01", DateTimeOffset.UtcNow, CancellationToken.None);
                Equal(2, result.RawDois.Length); Equal("10.1234/a", result.RawDois[0]);
                True(result.Snapshot.Complete); Equal("CompleteOneFacultyRead", result.Snapshot.EndMarker);
                Equal("Synthetic Faculty", result.Faculty.FacultyName);
            });
            await Check("Known faculty with zero DOI rows remains known", async () =>
            {
                using var reader = Records([]);
                var result = await DatabaseReader.ParseAsync(reader, "demo01", DateTimeOffset.UtcNow, CancellationToken.None);
                Equal(0, result.RawDois.Length); Equal(1, result.Snapshot.FacultyRows);
                Equal("demo01", result.Faculty.UserId);
            });
            await Check("Unknown faculty is not represented as zero publications", async () =>
            {
                using var reader = Records([], facultyCount: 0);
                await ThrowsAsync<FormatException>(() => DatabaseReader.ParseAsync(reader, "demo01", DateTimeOffset.UtcNow, CancellationToken.None));
            });
            await Check("SQL-record adapter rejects truncated rows and missing markers", async () =>
            {
                using var truncated = Records(["10.1234/a"], expectedDoiCount: 2);
                await ThrowsAsync<FormatException>(() => DatabaseReader.ParseAsync(truncated, "demo01", DateTimeOffset.UtcNow, CancellationToken.None));
                using var marker = Records(["10.1234/a"], marker: "WrongMarker");
                await ThrowsAsync<FormatException>(() => DatabaseReader.ParseAsync(marker, "demo01", DateTimeOffset.UtcNow, CancellationToken.None));
            });
            await Check("SQL-record adapter rejects wrong databases and identities", async () =>
            {
                using var wrongDb = Records([], database: "OtherDatabase");
                await ThrowsAsync<FormatException>(() => DatabaseReader.ParseAsync(wrongDb, "demo01", DateTimeOffset.UtcNow, CancellationToken.None));
                using var wrongId = Records(["10.1234/a"], userId: "other01");
                await ThrowsAsync<FormatException>(() => DatabaseReader.ParseAsync(wrongId, "demo01", DateTimeOffset.UtcNow, CancellationToken.None));
            });
            await Check("Null stored DOI association is retained as invalid data", async () =>
            {
                using var reader = Records([null]);
                var result = await DatabaseReader.ParseAsync(reader, "demo01", DateTimeOffset.UtcNow, CancellationToken.None);
                Equal(1, result.RawDois.Length); True(result.RawDois[0] is null);
                Equal("InvalidDoi", DoiRules.Plan(result, [])[0].Status);
            });
            await Check("Metadata preserves author order and repeated family names", () =>
            {
                var (csl, errors) = CrossrefMetadataReader.Parse(Metadata(), "10.1234/paper");
                Equal(0, errors.Length); True(csl is not null);
                var authors = csl!.Value.GetProperty("author");
                Equal(3, authors.GetArrayLength()); Equal("First", authors[0].GetProperty("family").GetString());
                Equal("Second", authors[1].GetProperty("given").GetString());
                Equal("Synthetic Research Group", authors[2].GetProperty("literal").GetString());
                Equal("doi:10.1234/paper", csl.Value.GetProperty("id").GetString());
            });
            await Check("Metadata never truncates a long author list", () =>
            {
                var authors = Enumerable.Range(1, 35).Select(i => new { family = "Author" + i, given = "Given" + i }).ToArray();
                var json = JsonSerializer.Serialize(new Dictionary<string, object>
                { ["DOI"] = "10.1234/paper", ["type"] = "article-journal", ["title"] = "Synthetic paper", ["container-title"] = "Synthetic journal",
                    ["issued"] = new Dictionary<string, int[][]> { ["date-parts"] = [[2025]] }, ["author"] = authors });
                var (csl, errors) = CrossrefMetadataReader.Parse(json, "10.1234/paper");
                Equal(0, errors.Length); Equal(35, csl!.Value.GetProperty("author").GetArrayLength());
                Equal("Author35", csl.Value.GetProperty("author")[34].GetProperty("family").GetString());
            });
            await Check("Wrong DOI metadata is never used for a citation", () =>
            {
                var (csl, errors) = CrossrefMetadataReader.Parse(Metadata("10.1234/other"), "10.1234/paper");
                True(csl is null); True(errors.Length > 0);
            });
            await Check("Missing ordered authors and dates remain incomplete", () =>
            {
                var json = """{"DOI":"10.1234/paper","type":"article-journal","title":"Synthetic","container-title":"Synthetic journal","issued":{"date-parts":[["2025"]]}}""";
                var (csl, errors) = CrossrefMetadataReader.Parse(json, "10.1234/paper");
                True(csl is not null); True(errors.Any(value => value.Contains("author list")));
                True(errors.Any(value => value.Contains("publication year")));
            });
            await Check("Invalid calendar dates and author field types remain incomplete", () =>
            {
                var badDate = Metadata().Replace("2025,11,10", "2025,2,30", StringComparison.Ordinal);
                var (_, dateErrors) = CrossrefMetadataReader.Parse(badDate, "10.1234/paper");
                True(dateErrors.Any(value => value.Contains("publication year/date")));
                var badAuthor = Metadata().Replace("\"given\":\"Initial\"", "\"given\":{}", StringComparison.Ordinal);
                var (_, nameErrors) = CrossrefMetadataReader.Parse(badAuthor, "10.1234/paper");
                True(nameErrors.Any(value => value.Contains("author name field")));
            });
            await Check("Duplicate JSON fields are held rather than silently replaced", () =>
            {
                var (csl, errors) = CrossrefMetadataReader.Parse("""{"DOI":"10.1234/paper","DOI":"10.1234/paper"}""", "10.1234/paper");
                True(csl is null); True(errors.Single().Contains("duplicate field"));
            });
            await Check("Malformed optional editors and translators cannot become ready citations", () =>
            {
                foreach (var field in new[] { "editor", "translator", "container-author", "collection-editor", "editor-translator", "commenter" })
                {
                    using var document = JsonDocument.Parse(Metadata());
                    var values = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value.Clone());
                    values[field] = new object[] { new { family = new { bad = true }, given = "Synthetic Editor" } };
                    var (_, errors) = CrossrefMetadataReader.Parse(JsonSerializer.Serialize(values), "10.1234/paper");
                    True(errors.Any(value => value.Contains("optional contributor")));
                    values[field] = new { family = "WrongShape" };
                    var (_, shapeErrors) = CrossrefMetadataReader.Parse(JsonSerializer.Serialize(values), "10.1234/paper");
                    True(shapeErrors.Any(value => value.Contains("optional contributor list")));
                }
            });
            await Check("Crossref uses one fixed HTTPS endpoint and public DOI only", async () =>
            {
                var handler = new Handler(HttpStatusCode.OK, Metadata());
                using var http = new HttpClient(handler);
                var source = new CrossrefMetadataReader(http, spacing: TimeSpan.Zero);
                var result = await source.EnrichAsync(Entry(), CancellationToken.None);
                Equal("Ready", result.Status); True(result.PreviewEligible);
                Equal("api.crossref.org", handler.Requests.Single().Host);
                True(!handler.Requests[0].AbsoluteUri.Contains("demo01", StringComparison.Ordinal));
                True(result.MetadataSource!.BodySha256.Length == 64); Equal(1, source.Requests.Count);
            });
            await Check("Held associations cannot be passed to metadata enrichment", async () =>
            {
                var handler = new Handler(HttpStatusCode.OK, Metadata());
                using var http = new HttpClient(handler);
                var source = new CrossrefMetadataReader(http, spacing: TimeSpan.Zero);
                await ThrowsAsync<InvalidOperationException>(() => source.EnrichAsync(Entry() with { Status = "Held", ReviewDisposition = "Held" }, CancellationToken.None));
                Equal(0, handler.Requests.Count);
            });
            await Check("Missing Crossref metadata differs from a failed provider", async () =>
            {
                using var absentHttp = new HttpClient(new Handler(HttpStatusCode.NotFound, "not shared"));
                var absent = new CrossrefMetadataReader(absentHttp, spacing: TimeSpan.Zero);
                Equal("MetadataUnavailable", (await absent.EnrichAsync(Entry(), CancellationToken.None)).Status); True(!absent.Blocked);
                using var failedHttp = new HttpClient(new Handler(HttpStatusCode.ServiceUnavailable, "private upstream body"));
                var failed = new CrossrefMetadataReader(failedHttp, spacing: TimeSpan.Zero);
                var result = await failed.EnrichAsync(Entry(), CancellationToken.None);
                Equal("MetadataError", result.Status); True(failed.Blocked);
                True(!result.Errors.Single().Contains("private upstream body"));
            });
            await Check("Rate limits stop additional source requests", async () =>
            {
                using var http = new HttpClient(new Handler(HttpStatusCode.TooManyRequests, "ignored"));
                var source = new CrossrefMetadataReader(http, spacing: TimeSpan.Zero);
                var result = await source.EnrichAsync(Entry(), CancellationToken.None);
                Equal("MetadataError", result.Status); True(source.Blocked); True(!result.PreviewEligible);
            });
            await Check("Malformed and oversize source responses cannot become citations", async () =>
            {
                using var badHttp = new HttpClient(new Handler(HttpStatusCode.OK, "not JSON"));
                var bad = new CrossrefMetadataReader(badHttp, spacing: TimeSpan.Zero);
                Equal("MetadataError", (await bad.EnrichAsync(Entry(), CancellationToken.None)).Status);
                using var hugeHttp = new HttpClient(new Handler(HttpStatusCode.OK, new string('x', 2_000_001)));
                var huge = new CrossrefMetadataReader(hugeHttp, spacing: TimeSpan.Zero);
                Equal("MetadataError", (await huge.EnrichAsync(Entry(), CancellationToken.None)).Status);
            });
            await Check("Cancellation leaves a reportable incomplete metadata state", async () =>
            {
                var handler = new Handler(HttpStatusCode.OK, Metadata()); using var http = new HttpClient(handler);
                var source = new CrossrefMetadataReader(http, spacing: TimeSpan.Zero);
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                var result = await source.EnrichAsync(Entry(), cancellation.Token);
                Equal("MetadataError", result.Status); True(!result.PreviewEligible); Equal(0, handler.Requests.Count);
            });
            await Check("Reports retain all source rows and use one eligible CSL item set", async () =>
            {
                var read = Snapshot(["10.1234/paper", "doi:10.1234/paper", "10.1234/held", "bad DOI", "10.1234/not-requested"]);
                var entries = DoiRules.Plan(read, [new("demo01", "10.1234/held", "Synthetic hold", null)]);
                using var http = new HttpClient(new Handler(HttpStatusCode.OK, Metadata()));
                var source = new CrossrefMetadataReader(http, spacing: TimeSpan.Zero);
                entries[0] = await source.EnrichAsync(entries[0], CancellationToken.None);
                var report = DoiRules.Report(read, entries, source.Requests, 1, DateTimeOffset.UtcNow, false, false);
                Equal(5, report.Publications.Length); Equal(1, report.CslItems.Length);
                Equal(1, report.Summary.HeldAssociationRows); Equal(1, report.Summary.DuplicateAssociationRows);
                Equal(1, report.Summary.InvalidDoiRows); Equal(1, report.Summary.MetadataNotRequestedCount);
                True(!report.Summary.MetadataCoverageComplete); Equal(0, report.SqlWrites); Equal("NotPerformed", report.AuthorshipReview);
                Equal(report.Publications[0].Csl!.Value.GetRawText(), report.CslItems[0].GetRawText());
            });
            await Check("Snapshot-only mode claims no metadata completion", () =>
            {
                var read = Snapshot(["10.1234/paper"]); var entries = DoiRules.Plan(read, []);
                var report = DoiRules.Report(read, entries, [], 25, DateTimeOffset.UtcNow, true, false);
                Equal(0, report.CslItems.Length); Equal(0, report.MetadataLookups.Length);
                True(report.Notices.Any(value => value.StartsWith("Snapshot-only", StringComparison.Ordinal)));
                True(!report.Summary.MetadataCoverageComplete);
            });
            await Check("JSON embedding escapes source HTML/script delimiters", () =>
            {
                var json = JsonSerializer.Serialize(new { title = "</script><script>alert('test')</script>" }, Program.JsonOptions);
                True(!json.Contains("</script>", StringComparison.OrdinalIgnoreCase));
                True(json.Contains("\\u003C", StringComparison.Ordinal));
            });
            await Check("CLI rejects repeated/unknown/missing options and internal export paths", () =>
            {
                Equal("demo01", Program.ParseArguments(["--user", "demo01", "--open"])["--user"]);
                Throws<FormatException>(() => Program.ParseArguments(["--user"]));
                Throws<FormatException>(() => Program.ParseArguments(["--user", "demo01", "--user", "other01"]));
                Throws<FormatException>(() => Program.ParseArguments(["--import"]));
                var root = Path.Combine(Path.GetTempPath(), "citation-pilot-source");
                True(Program.Within(Path.Combine(root, "exports"), root)); True(Program.Within(root, root));
                True(!Program.Within(root + "-other", root));
            });
            Console.WriteLine(_passed + " citation-pilot self-checks passed. SQL connections: 0; live HTTP requests: 0.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Citation-pilot self-check failed after " + _passed + " checks: " + ex.Message);
            return 1;
        }
    }

    private static PublicationEntry Entry() => DoiRules.Plan(Snapshot(["10.1234/paper"]), [])[0];
    private static DatabaseReadResult Snapshot(string?[] raw) => new(new(Guid.NewGuid(), "SyntheticServer", "AcademicOnline_Rebuild",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, raw.Length, true, "CompleteOneFacultyRead"),
        new("demo01", "Synthetic Faculty", "Synthetic department"), raw);

    private static DataTableReader Records(string?[] raw, long facultyCount = 1, long? expectedDoiCount = null,
        string marker = "CompleteOneFacultyRead", string database = "AcademicOnline_Rebuild", string userId = "demo01")
    {
        var header = new DataTable(); header.Columns.Add("ServerName", typeof(string)); header.Columns.Add("DatabaseName", typeof(string));
        header.Columns.Add("ReadUtc", typeof(DateTime)); header.Columns.Add("FacultyRows", typeof(long)); header.Columns.Add("DoiRows", typeof(long));
        header.Rows.Add("SyntheticServer", database, DateTime.UtcNow, facultyCount, expectedDoiCount ?? raw.Length);
        var rows = new DataTable(); foreach (var field in new[] { "UserId", "FacultyName", "PrimaryDepartment", "DoiOwner", "DOI" }) rows.Columns.Add(field, typeof(string));
        if (facultyCount > 0)
        {
            if (raw.Length == 0) rows.Rows.Add(userId, "Synthetic Faculty", "Synthetic department", DBNull.Value, DBNull.Value);
            else foreach (var value in raw) rows.Rows.Add(userId, "Synthetic Faculty", "Synthetic department", userId, (object?)value ?? DBNull.Value);
        }
        var ending = new DataTable(); ending.Columns.Add("EndMarker", typeof(string)); ending.Rows.Add(marker);
        return new DataTableReader([header, rows, ending]);
    }

    private static string Metadata(string doi = "10.1234/paper") => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["DOI"] = doi, ["type"] = "article-journal", ["title"] = "Synthetic publication", ["container-title"] = "Synthetic journal",
        ["volume"] = "12", ["page"] = "101-105", ["issued"] = new Dictionary<string, int[][]> { ["date-parts"] = [[2025, 11, 10]] },
        ["author"] = new object[] { new { family = "First", given = "Initial" }, new { family = "First", given = "Second" }, new { literal = "Synthetic Research Group" } }
    });

    private static async Task Check(string name, Action action) { action(); _passed++; Console.WriteLine("PASS " + name); await Task.CompletedTask; }
    private static async Task Check(string name, Func<Task> action) { await action(); _passed++; Console.WriteLine("PASS " + name); }
    private static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status; private readonly string _body;
        public List<Uri> Requests { get; } = new();
        public Handler(HttpStatusCode status, string body) { _status = status; _body = body; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Equal(HttpMethod.Get, request.Method); Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") });
        }
    }
}
