using System.Diagnostics;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace AcademicOnline.CitationPilot;

internal static class Program
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        if (args is ["--self-test"]) return await SelfChecks.RunAsync();
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine("Usage: dotnet run --project tools/AcademicOnline.CitationPilot -- --user jec9e [--max-metadata 25] [--snapshot-only] [--output OUTSIDE_APP_FOLDER] [--mailto CONTACT] [--open]");
            return 0;
        }
        var started = DateTimeOffset.UtcNow;
        try
        {
            var options = ParseArguments(args);
            var root = Path.GetFullPath(options.GetValueOrDefault("--app-root")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "AcademicOnline"));
            var configFolder = Path.Combine(root, "src", "AcademicOnline.FirstPage");
            if (!File.Exists(Path.Combine(configFolder, "AcademicOnline.FirstPage.csproj")))
                throw new FormatException("The designated working app was not found at " + root + ". Use --app-root only for an explicitly selected installation.");
            var id = DoiRules.UserId(options.GetValueOrDefault("--user") ?? "jec9e");
            if (!int.TryParse(options.GetValueOrDefault("--max-metadata") ?? "25", out var budget) || budget is < 0 or > 500)
                throw new FormatException("--max-metadata must be between 0 and 500. Every stored DOI row is exported regardless of this metadata-request budget.");
            var mailTo = options.GetValueOrDefault("--mailto");
            if (mailTo is not null && (!MailAddress.TryCreate(mailTo, out var address) || address.Address != mailTo))
                throw new FormatException("--mailto must be an application contact email address, without a display name.");
            var outputRoot = Path.GetFullPath(options.GetValueOrDefault("--output")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AcademicOnline", "CitationPilot"));
            if (Within(outputRoot, root)) throw new FormatException("Export folders must be outside the application/source folder. This keeps faculty exports out of source-control updates.");
            var configuration = new ConfigurationBuilder().SetBasePath(configFolder)
                .AddJsonFile("appsettings.json", optional: false).AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables().Build();
            var connectionString = configuration.GetConnectionString("FacultyDatabase");
            if (string.IsNullOrWhiteSpace(connectionString)) throw new FormatException("The working FacultyDatabase connection is not configured.");
            var rules = configuration.GetSection("AcademicOnline:DoiReviewRules").Get<DoiReviewRule[]>() ?? [];
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += onCancel;
            try
            {
                Console.WriteLine("Reading the stored DOI associations for " + id + ". SQL writes: 0.");
                var snapshot = await DatabaseReader.ReadAsync(connectionString, id, cancellation.Token);
                var entries = DoiRules.Plan(snapshot, rules);
                var snapshotOnly = options.ContainsKey("--snapshot-only");
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                var source = new CrossrefMetadataReader(http, mailTo);
                for (var i = 0; i < entries.Length; i++)
                {
                    if (entries[i].Status != "MetadataNotRequested") continue;
                    if (snapshotOnly || source.Requests.Count >= budget || source.Blocked || cancellation.IsCancellationRequested)
                    {
                        var reason = snapshotOnly ? "Snapshot-only mode: no metadata request was made."
                            : cancellation.IsCancellationRequested ? "Metadata lookup was cancelled before this entry."
                            : source.Blocked ? "Not requested after a source access/rate-limit/connection failure."
                            : "Not requested because the configured metadata-request budget was reached.";
                        entries[i] = entries[i] with { Errors = [reason] };
                        continue;
                    }
                    entries[i] = await source.EnrichAsync(entries[i], cancellation.Token);
                    Console.WriteLine("Metadata " + source.Requests.Count + "/" + budget + ": " + entries[i].NormalizedDoi + " — " + entries[i].Status);
                }
                var report = DoiRules.Report(snapshot, entries, source.Requests, budget, started, snapshotOnly, cancellation.IsCancellationRequested);
                var outputFolder = Path.Combine(outputRoot, started.ToString("yyyyMMdd_HHmmss") + "_" + id + "_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(outputFolder);
                var json = JsonSerializer.Serialize(report, JsonOptions);
                var jsonPath = Path.Combine(outputFolder, "publication-citations.json");
                await File.WriteAllTextAsync(jsonPath, json);
                await File.WriteAllTextAsync(Path.Combine(outputFolder, "csl-items.json"), JsonSerializer.Serialize(report.CslItems, JsonOptions));
                var templatePath = Path.Combine(AppContext.BaseDirectory, "assets", "preview-template.html");
                if (!File.Exists(templatePath)) templatePath = Path.Combine(root, "tools", "AcademicOnline.CitationPilot", "assets", "preview-template.html");
                string? previewPath = null;
                if (File.Exists(templatePath))
                {
                    var template = await File.ReadAllTextAsync(templatePath);
                    if (!template.Contains("__CITATION_EXPORT_JSON__", StringComparison.Ordinal)) throw new FormatException("The citation preview template does not contain its data placeholder.");
                    previewPath = Path.Combine(outputFolder, "preview.html");
                    // Default JSON encoding escapes '<', so source text cannot close the application/json script element.
                    await File.WriteAllTextAsync(previewPath, template.Replace("__CITATION_EXPORT_JSON__", json, StringComparison.Ordinal));
                }
                Console.WriteLine("Stored DOI rows exported: " + report.Summary.SourceAssociationRows + "; preview citations: " + report.Summary.ReadyCitationCount + "; SQL writes: 0.");
                Console.WriteLine("JSON export: " + jsonPath);
                if (previewPath is not null) Console.WriteLine("Three-style preview: " + previewPath);
                else Console.WriteLine("Preview template was not found. The JSON export is available; install the complete citation-pilot package to generate the offline preview.");
                if (options.ContainsKey("--open") && previewPath is not null)
                {
                    try { Process.Start(new ProcessStartInfo(previewPath) { UseShellExecute = true }); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                    { Console.WriteLine("Open the saved preview.html in your browser to inspect the citation styles."); }
                }
                return report.Summary.MetadataCoverageComplete && !cancellation.IsCancellationRequested ? 0 : 2;
            }
            finally { Console.CancelKeyPress -= onCancel; }
        }
        catch (SqlException ex)
        { Console.Error.WriteLine("The faculty database could not be read (SQL error " + ex.Number + "). Check the existing connection and SELECT access. No database rows were changed."); return 1; }
        catch (OperationCanceledException)
        { Console.Error.WriteLine("The database read was cancelled before a complete snapshot was obtained. No complete export was claimed."); return 1; }
        catch (FormatException ex) { Console.Error.WriteLine(ex.Message); return 1; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Console.Error.WriteLine("The working configuration or output files could not be read/written. Check file permissions, paths and JSON. No database rows were changed."); return 1; }
    }

    internal static Dictionary<string, string?> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            if (name is not ("--user" or "--max-metadata" or "--snapshot-only" or "--output" or "--mailto" or "--app-root" or "--open") || result.ContainsKey(name))
                throw new FormatException("Unknown or repeated citation-pilot option: " + name);
            if (name is "--snapshot-only" or "--open") result.Add(name, null);
            else
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new FormatException("Missing value for " + name);
                result.Add(name, args[i]);
            }
        }
        return result;
    }

    internal static bool Within(string path, string root)
    {
        path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
