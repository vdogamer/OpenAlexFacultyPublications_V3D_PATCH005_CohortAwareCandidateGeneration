using System.Text.Json;
using System.Text.RegularExpressions;

namespace AcademicOnline.CitationPilot;

internal sealed record FacultyIdentity(string UserId, string FacultyName, string PrimaryDepartment);

internal sealed record DatabaseSnapshot(Guid SnapshotId, string ServerName, string DatabaseName,
    DateTimeOffset ReadStartedUtc, DateTimeOffset ReadCompletedUtc, int FacultyRows, int DoiRows,
    bool Complete, string EndMarker);

internal sealed record DatabaseReadResult(DatabaseSnapshot Snapshot, FacultyIdentity Faculty, string?[] RawDois);

internal sealed record DoiReviewRule(string UserId, string Doi, string Reason, string? ReferenceUrl);

internal sealed record MetadataSource(string Provider, string Url, DateTimeOffset ReadUtc, string BodySha256);

internal sealed record PublicationEntry(int SourceRow, string? RawDoi, string? NormalizedDoi,
    string Status, string ReviewDisposition, bool PreviewEligible, string[] ReviewReasons,
    string[] Errors, JsonElement? Csl = null, MetadataSource? MetadataSource = null,
    int? DuplicateOfSourceRow = null);

internal sealed record MetadataRequest(string Doi, string Url, DateTimeOffset StartedUtc,
    string Status, int? HttpStatusCode, string? BodySha256, string Detail);

internal sealed record ExportSummary(int SourceAssociationRows, int InvalidDoiRows, int DistinctValidDois,
    int HeldAssociationRows, int HeldDistinctDois, int DuplicateAssociationRows, int EligibleDistinctDois,
    int MetadataLookupsAttempted, int ReadyCitationCount, int MetadataIncompleteCount,
    int MetadataFailureCount, int MetadataNotRequestedCount, int MaxMetadataLookups, bool MetadataCoverageComplete);

internal sealed record CitationExport(string SchemaVersion, DateTimeOffset StartedUtc, DateTimeOffset CompletedUtc,
    bool ReadOnly, int SqlWrites, string AuthorshipReview, string Scope,
    FacultyIdentity Faculty, DatabaseSnapshot DatabaseSnapshot, ExportSummary Summary,
    PublicationEntry[] Publications, JsonElement[] CslItems, MetadataRequest[] MetadataLookups,
    string[] Notices);

internal static class DoiRules
{
    private static readonly Regex Prefix = new(@"\A(?:doi:\s*|https?://(?:dx\.)?doi\.org/)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Pattern = new(@"\A10\.[0-9]{4,9}/[^\s\p{C}]+\z", RegexOptions.CultureInvariant);

    public static string Normalize(string? raw) => Prefix.Replace((raw ?? string.Empty).Trim(), string.Empty, 1).Trim().ToLowerInvariant();
    public static bool Valid(string value) => value.Length is > 0 and <= 400 && Pattern.IsMatch(value);

    public static string UserId(string? value)
    {
        var id = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (id.EndsWith("@virginia.edu", StringComparison.Ordinal)) id = id[..^13];
        return Regex.IsMatch(id, @"\A[a-z0-9]{2,32}\z") ? id
            : throw new FormatException("Enter one valid UVA ComputingID or its @virginia.edu address.");
    }

    public static PublicationEntry[] Plan(DatabaseReadResult read, IReadOnlyList<DoiReviewRule> rules)
    {
        if (!read.Snapshot.Complete || read.Snapshot.EndMarker != "CompleteOneFacultyRead"
            || read.Snapshot.FacultyRows != 1 || read.Snapshot.DoiRows != read.RawDois.Length)
            throw new FormatException("The one-faculty database read is incomplete. No complete publication export can be claimed.");
        var result = new List<PublicationEntry>();
        var firstRows = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in read.RawDois)
        {
            var row = result.Count + 1;
            var doi = Normalize(raw);
            var valid = Valid(doi);
            int? duplicate = valid && firstRows.TryGetValue(doi, out var prior) ? prior : null;
            if (valid && duplicate is null) firstRows.Add(doi, row);
            var holds = rules.Where(rule => string.Equals(rule.UserId?.Trim(), read.Faculty.UserId, StringComparison.OrdinalIgnoreCase)
                    && Normalize(rule.Doi) == doi)
                .Select(rule => (string.IsNullOrWhiteSpace(rule.Reason) ? "Held by a configured DOI review rule." : rule.Reason.Trim())
                    + (string.IsNullOrWhiteSpace(rule.ReferenceUrl) ? string.Empty : " Reference: " + rule.ReferenceUrl.Trim()))
                .ToArray();
            var held = holds.Length > 0;
            result.Add(new PublicationEntry(row, raw, valid ? doi : null,
                held ? "Held" : !valid ? "InvalidDoi" : duplicate is not null ? "DuplicateAssociation" : "MetadataNotRequested",
                held ? "Held" : "StoredAssociationNotIndependentlyReviewed", false, holds,
                valid ? [] : ["The stored value does not meet the existing application's basic DOI format/storage-length rules. It was retained without correction."],
                DuplicateOfSourceRow: duplicate));
        }
        return result.ToArray();
    }

    public static CitationExport Report(DatabaseReadResult read, PublicationEntry[] entries,
        IReadOnlyList<MetadataRequest> requests, int maxMetadata, DateTimeOffset startedUtc,
        bool snapshotOnly, bool cancelled)
    {
        var valid = entries.Where(row => row.NormalizedDoi is not null).ToArray();
        var eligible = entries.Where(row => row.Status is not ("Held" or "InvalidDoi" or "DuplicateAssociation")).ToArray();
        var ready = eligible.Where(row => row.PreviewEligible && row.Status == "Ready" && row.Csl is not null
            && row.ReviewDisposition == "StoredAssociationNotIndependentlyReviewed").ToArray();
        var summary = new ExportSummary(entries.Length, entries.Count(row => row.NormalizedDoi is null),
            valid.Select(row => row.NormalizedDoi).Distinct().Count(), entries.Count(row => row.Status == "Held"),
            entries.Where(row => row.Status == "Held" && row.NormalizedDoi is not null).Select(row => row.NormalizedDoi).Distinct().Count(),
            entries.Count(row => row.DuplicateOfSourceRow is not null), eligible.Length, requests.Count, ready.Length,
            entries.Count(row => row.Status == "MetadataIncomplete"),
            entries.Count(row => row.Status is "MetadataError" or "MetadataUnavailable"),
            entries.Count(row => row.Status == "MetadataNotRequested"), maxMetadata, ready.Length == eligible.Length);
        var notices = new List<string>
        {
            "This complete database snapshot is scoped to one faculty member's stored DOI associations. It is not a complete career bibliography.",
            "Stored associations and external bibliographic metadata are not independent confirmation of faculty authorship. No publication was accepted, imported or changed.",
            "All three citation styles use exactly the same eligible CSL-JSON items, preserving provider author order. Held, invalid, duplicate and incomplete entries remain in the export but are not ordinary preview citations.",
            "Crossref is one metadata source. A missing or failed Crossref response does not establish that a publication or DOI is absent elsewhere."
        };
        if (snapshotOnly) notices.Add("Snapshot-only mode: no external metadata requests were made.");
        if (!summary.MetadataCoverageComplete) notices.Add("Citation metadata coverage is partial. Inspect each publication's status; all stored DOI rows were still retained.");
        if (cancelled) notices.Add("Metadata enrichment was cancelled. The complete, previously finished SQL read remains exported; unqueried entries are identified.");
        return new("AcademicOnline.CitationPilot/1", startedUtc, DateTimeOffset.UtcNow, true, 0,
            "NotPerformed", "OneFacultyStoredDoiAssociations", read.Faculty, read.Snapshot, summary,
            entries, ready.Select(row => row.Csl!.Value.Clone()).ToArray(), requests.ToArray(), notices.ToArray());
    }
}
