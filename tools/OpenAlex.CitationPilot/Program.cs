using System.Diagnostics;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace OpenAlexIngestion.CitationPilot;

internal static class Program
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        if (args is ["--self-test"]) return await SelfChecks.RunAsync();
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine("Usage: dotnet run --project tool -- --package-root . --user jec9e [--max-metadata 25] [--snapshot-only] [--output OUTSIDE_PACKAGE] [--mailto CONTACT] [--open]");
            Console.WriteLine("Database: OpenAlexIngestion_Test only. SQL writes: 0.");
            return 0;
        }
        var started = DateTimeOffset.UtcNow;
        try
        {
            var options = ParseArguments(args);
            var root = Path.GetFullPath(options.GetValueOrDefault("--package-root") ?? Directory.GetCurrentDirectory());
            if (!File.Exists(Path.Combine(root, "citation-settings.json")))
                throw new FormatException("citation-settings.json was not found. Extract the complete standalone package and run its root launcher, or pass --package-root explicitly.");
            var configuration = new ConfigurationBuilder().SetBasePath(root)
                .AddJsonFile("citation-settings.json", optional: false).AddEnvironmentVariables().Build();
            var id = DoiRules.UserId(options.GetValueOrDefault("--user") ?? "jec9e");
            var limitText = options.GetValueOrDefault("--max-metadata")
                ?? configuration["CitationPilot:DefaultMaxMetadata"] ?? "25";
            if (!int.TryParse(limitText, out var budget) || budget is < 0 or > 500)
                throw new FormatException("--max-metadata must be between 0 and 500. All returned source publication associations are exported separately from this request budget.");
            var mailTo = options.GetValueOrDefault("--mailto") ?? configuration["CitationPilot:MetadataContactEmail"];
            if (string.IsNullOrWhiteSpace(mailTo)) mailTo = null;
            if (mailTo is not null && (!MailAddress.TryCreate(mailTo, out var address) || address.Address != mailTo))
                throw new FormatException("The metadata contact must be an email address without a display name.");
            var outputRoot = Path.GetFullPath(options.GetValueOrDefault("--output")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OpenAlexIngestion", "CitationPilot"));
            if (Within(outputRoot, root))
                throw new FormatException("Choose an output folder outside the extracted package. This keeps faculty data separate from code updates.");
            var connectionString = Environment.GetEnvironmentVariable("OPENALEX_CITATION_CONNECTION")
                ?? configuration.GetConnectionString("OpenAlexDatabase");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new FormatException("Configure ConnectionStrings:OpenAlexDatabase in citation-settings.json, or set OPENALEX_CITATION_CONNECTION locally.");
            SqlConnectionStringBuilder connectionSettings;
            try { connectionSettings = new SqlConnectionStringBuilder(connectionString); }
            catch (ArgumentException) { throw new FormatException("The citation database connection string is not valid. Its value was not printed."); }
            if (!connectionSettings.InitialCatalog.Equals("OpenAlexIngestion_Test", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("This standalone pilot requires Database=OpenAlexIngestion_Test. It does not target AcademicOnline_Rebuild or another database.");
            var rules = configuration.GetSection("CitationPilot:DoiReviewRules").Get<DoiReviewRule[]>() ?? [];
            var templatePath = Path.Combine(AppContext.BaseDirectory, "assets", "preview-template.html");
            if (!File.Exists(templatePath)) templatePath = Path.Combine(root, "tool", "assets", "preview-template.html");
            if (!File.Exists(templatePath))
                throw new FormatException("The offline preview template is missing. Extract the complete citation-pilot ZIP before running it.");
            var template = await File.ReadAllTextAsync(templatePath);
            if (template.Split("__CITATION_EXPORT_JSON__", StringSplitOptions.None).Length != 2)
                throw new FormatException("The offline preview template has an invalid data placeholder. Use the complete tested download.");

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += onCancel;
            try
            {
                Console.WriteLine("Target database: OpenAlexIngestion_Test. Selected ComputingID: " + id + ". SQL writes: 0.");
                var snapshot = await DatabaseReader.ReadAsync(connectionString, id, cancellation.Token);
                foreach (var source in snapshot.Snapshot.SourceChecks)
                    Console.WriteLine(source.Source + ": " + source.Status + "; publication associations=" + source.PublicationRows);
                if (!snapshot.Snapshot.Complete)
                    Console.WriteLine("PARTIAL SOURCE READ: source problems will remain explicit in the saved report.");
                var entries = DoiRules.Plan(snapshot, rules);
                var snapshotOnly = options.ContainsKey("--snapshot-only");
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                var metadata = new CrossrefMetadataReader(http, mailTo);
                for (var i = 0; i < entries.Length; i++)
                {
                    if (entries[i].Status != "MetadataNotRequested") continue;
                    if (snapshotOnly || metadata.Requests.Count >= budget || metadata.Blocked || cancellation.IsCancellationRequested)
                    {
                        var reason = snapshotOnly ? "Snapshot-only mode: no metadata request was made."
                            : cancellation.IsCancellationRequested ? "Metadata lookup was cancelled before this entry."
                            : metadata.Blocked ? "Not requested after a metadata source access/rate-limit/connection failure."
                            : "Not requested because the metadata-request budget was reached.";
                        entries[i] = entries[i] with { Errors = [reason] };
                        continue;
                    }
                    entries[i] = await metadata.EnrichAsync(entries[i], cancellation.Token);
                    Console.WriteLine("Metadata " + metadata.Requests.Count + "/" + budget + ": " + entries[i].NormalizedDoi + " - " + entries[i].Status);
                }
                var report = DoiRules.Report(snapshot, entries, metadata.Requests, budget, started,
                    snapshotOnly, cancellation.IsCancellationRequested);
                var outputFolder = Path.Combine(outputRoot,
                    started.ToString("yyyyMMdd_HHmmss") + "_" + id + "_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(outputFolder);
                var json = JsonSerializer.Serialize(report, JsonOptions);
                var jsonPath = Path.Combine(outputFolder, "publication-citations.json");
                var previewPath = Path.Combine(outputFolder, "preview.html");
                await File.WriteAllTextAsync(jsonPath, json);
                await File.WriteAllTextAsync(Path.Combine(outputFolder, "csl-items.json"),
                    JsonSerializer.Serialize(report.CslItems, JsonOptions));
                // Normal System.Text.Json encoding escapes '<', preventing source strings from closing the JSON script.
                await File.WriteAllTextAsync(previewPath,
                    template.Replace("__CITATION_EXPORT_JSON__", json, StringComparison.Ordinal));
                Console.WriteLine("Returned source publication associations: " + report.Summary.SourceAssociationRows
                    + "; preview citations: " + report.Summary.ReadyCitationCount + "; SQL writes: 0.");
                Console.WriteLine("Database source read complete: " + report.DatabaseSnapshot.Complete);
                Console.WriteLine("JSON export: " + jsonPath);
                Console.WriteLine("AMA / Vancouver / APA preview: " + previewPath);
                if (options.ContainsKey("--open"))
                {
                    try { Process.Start(new ProcessStartInfo(previewPath) { UseShellExecute = true }); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                    { Console.WriteLine("Open the saved preview.html in your browser to inspect the three citation styles."); }
                }
                return report.DatabaseSnapshot.Complete && report.Summary.MetadataCoverageComplete
                    && !cancellation.IsCancellationRequested ? 0 : 2;
            }
            finally { Console.CancelKeyPress -= onCancel; }
        }
        catch (SqlException ex)
        { Console.Error.WriteLine("OpenAlexIngestion_Test could not be read (SQL error " + ex.Number + "). Check the local server and SELECT access. No database rows were changed."); return 1; }
        catch (OperationCanceledException)
        { Console.Error.WriteLine("The database read was cancelled before a report could be saved. No completed export was claimed."); return 1; }
        catch (FormatException ex) { Console.Error.WriteLine(ex.Message); return 1; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Console.Error.WriteLine("The standalone settings or output files could not be read/written. Check the extracted package, paths, JSON and folder permissions."); return 1; }
    }

    internal static Dictionary<string, string?> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            if (name is not ("--user" or "--max-metadata" or "--snapshot-only" or "--output" or "--mailto" or "--package-root" or "--open")
                || result.ContainsKey(name)) throw new FormatException("Unknown or repeated citation-pilot option: " + name);
            if (name is "--snapshot-only" or "--open") result.Add(name, null);
            else
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new FormatException("Missing value for " + name);
                result.Add(name, args[i]);
            }
        }
        return result;
    }

    internal static bool Within(string path, string root)
    {
        path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.Equals(root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
