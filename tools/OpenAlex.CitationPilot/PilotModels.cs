using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenAlexIngestion.CitationPilot;

internal sealed record FacultyIdentity(string UserId, string FacultyName, string PrimaryDepartment,
    string[]? Names = null, string[]? Departments = null);

internal sealed record SourceCheck(string Source, string Status, int RowsRead, int PublicationRows,
    string? Error = null);

internal sealed record SourceRow(string UserId, string FacultyName, string? PrimaryDepartment,
    string? RawDoi, string? StoredTitle, int? StoredYear, string Source, string SourceLabel,
    string SourceRecordKey, long? NativeFacultyId, bool HasPublication,
    string ReviewDisposition, string IdentityBasis, bool IdentityConflict = false);

internal sealed record DatabaseSnapshot(Guid SnapshotId, string ServerName, string DatabaseName,
    DateTimeOffset ReadStartedUtc, DateTimeOffset ReadCompletedUtc, int FacultyRows, int DoiRows,
    bool Complete, string EndMarker, SourceCheck[] SourceChecks);

internal sealed record DatabaseReadResult(DatabaseSnapshot Snapshot, FacultyIdentity Faculty, SourceRow[] Rows)
{
    public SourceRow[] PublicationRows => Rows.Where(row => row.HasPublication).ToArray();
    public string?[] RawDois => PublicationRows.Select(row => row.RawDoi).ToArray();
}

internal sealed record DoiReviewRule(string UserId, string Doi, string Reason, string? ReferenceUrl = null);

internal sealed record MetadataSource(string Provider, string Url, DateTimeOffset ReadUtc, string BodySha256,
    string[]? NormalizationNotes = null);

internal sealed record PublicationEntry(int SourceRow, string? RawDoi, string? NormalizedDoi,
    string Status, string ReviewDisposition, bool PreviewEligible, string[] ReviewReasons,
    string[] Errors, JsonElement? Csl = null, MetadataSource? MetadataSource = null,
    int? DuplicateOfSourceRow = null, string? Source = null, string? SourceLabel = null,
    string? SourceRecordKey = null, string? StoredTitle = null, int? StoredYear = null,
    string? IdentityBasis = null, string? SourceReviewDisposition = null);

internal sealed record MetadataRequest(string Doi, string Url, DateTimeOffset StartedUtc,
    string Status, int? HttpStatusCode, string? BodySha256, string Detail);

internal sealed record ExportSummary(int SourceAssociationRows, int InvalidDoiRows, int DistinctValidDois,
    int HeldAssociationRows, int HeldDistinctDois, int DuplicateAssociationRows, int EligibleDistinctDois,
    int MetadataLookupsAttempted, int ReadyCitationCount, int MetadataIncompleteCount,
    int MetadataFailureCount, int MetadataNotRequestedCount, int MaxMetadataLookups, bool MetadataCoverageComplete,
    int MissingDoiRows);

internal sealed record CitationExport(string SchemaVersion, DateTimeOffset StartedUtc, DateTimeOffset CompletedUtc,
    bool ReadOnly, int SqlWrites, string AuthorshipReview, string Scope,
    FacultyIdentity Faculty, DatabaseSnapshot DatabaseSnapshot, ExportSummary Summary,
    PublicationEntry[] Publications, JsonElement[] CslItems, MetadataRequest[] MetadataLookups,
    string[] Notices, SourceRow[]? IdentityEvidence = null);

internal static class DoiRules
{
    private static readonly Regex Prefix = new(@"\A(?:doi:\s*|https?://(?:dx\.)?doi\.org/)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Pattern = new(@"\A10\.[0-9]{4,9}/[^\s\p{C}]+\z", RegexOptions.CultureInvariant);

    public static string Normalize(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        var isDoiUrl = Regex.IsMatch(value, @"\Ahttps?://(?:dx\.)?doi\.org/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        value = Prefix.Replace(value, string.Empty, 1).Trim();
        if (isDoiUrl) value = Uri.UnescapeDataString(value);
        return value.ToLowerInvariant();
    }
    public static bool Valid(string value) => value.Length is > 0 and <= 2048 && Pattern.IsMatch(value);

    public static string UserId(string? value)
    {
        var id = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (id.EndsWith("@virginia.edu", StringComparison.Ordinal)) id = id[..^13];
        return Regex.IsMatch(id, @"\A[a-z0-9]{2,32}\z") ? id
            : throw new FormatException("Enter one valid UVA ComputingID or its @virginia.edu address.");
    }

    public static PublicationEntry[] Plan(DatabaseReadResult read, IReadOnlyList<DoiReviewRule> rules)
    {
        var failedSources = read.Snapshot.SourceChecks.Any(source => source.Status == "Failed");
        if (read.Snapshot.Complete == failedSources
            || read.Snapshot.EndMarker != (failedSources ? "PartialOneFacultyRead" : "CompleteOneFacultyRead")
            || read.Snapshot.FacultyRows != 1 || read.Snapshot.DoiRows != read.RawDois.Length)
            throw new FormatException("The one-faculty database read is incomplete. No complete publication export can be claimed.");
        var result = new List<PublicationEntry>();
        var firstRows = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var sourceRow in read.PublicationRows)
        {
            var raw = sourceRow.RawDoi;
            var row = result.Count + 1;
            var doi = Normalize(raw);
            var valid = Valid(doi);
            int? duplicate = valid && firstRows.TryGetValue(doi, out var prior) ? prior : null;
            if (valid && duplicate is null) firstRows.Add(doi, row);
            var holds = rules.Where(rule => string.Equals(rule.UserId?.Trim(), read.Faculty.UserId, StringComparison.OrdinalIgnoreCase)
                    && Normalize(rule.Doi) == doi)
                .Select(rule => (string.IsNullOrWhiteSpace(rule.Reason) ? "Held by a configured DOI review rule." : rule.Reason.Trim())
                    + (string.IsNullOrWhiteSpace(rule.ReferenceUrl) ? string.Empty : " Reference: " + rule.ReferenceUrl.Trim()))
                .ToList();
            if (sourceRow.IdentityConflict) holds.Add("The native faculty identity is conflicting. This source row was retained without selecting between native faculty records.");
            var held = holds.Count > 0;
            if (sourceRow.ReviewDisposition == "AcceptedOpenAlex" && sourceRow.Source != "AcceptedOpenAlex")
                throw new FormatException("An accepted-OpenAlex claim lacks its required accepted source provenance.");
            result.Add(new PublicationEntry(row, raw, valid ? doi : null,
                held ? "Held" : string.IsNullOrWhiteSpace(raw) ? "MissingDoi" : !valid ? "InvalidDoi" : duplicate is not null ? "DuplicateAssociation" : "MetadataNotRequested",
                held ? "Held" : sourceRow.ReviewDisposition, false, holds.ToArray(),
                valid ? [] : [string.IsNullOrWhiteSpace(raw) ? "The source publication has no stored DOI. It was retained, and this DOI metadata pilot did not infer an identifier."
                    : "The stored value does not meet the basic DOI syntax/2048-character pilot bound. It was retained without correction."],
                DuplicateOfSourceRow: duplicate, Source: sourceRow.Source, SourceLabel: sourceRow.SourceLabel,
                SourceRecordKey: sourceRow.SourceRecordKey, StoredTitle: sourceRow.StoredTitle,
                StoredYear: sourceRow.StoredYear, IdentityBasis: sourceRow.IdentityBasis,
                SourceReviewDisposition: sourceRow.ReviewDisposition));
        }
        return result.ToArray();
    }

    public static CitationExport Report(DatabaseReadResult read, PublicationEntry[] entries,
        IReadOnlyList<MetadataRequest> requests, int maxMetadata, DateTimeOffset startedUtc,
        bool snapshotOnly, bool cancelled)
    {
        var valid = entries.Where(row => row.NormalizedDoi is not null).ToArray();
        var eligible = entries.Where(row => row.Status is not ("Held" or "InvalidDoi" or "MissingDoi" or "DuplicateAssociation")).ToArray();
        var ready = eligible.Where(row => row.PreviewEligible && row.Status == "Ready" && row.Csl is not null
            && (row.ReviewDisposition == "StoredAssociationNotIndependentlyReviewed"
                || (row.ReviewDisposition == "AcceptedOpenAlex" && row.Source == "AcceptedOpenAlex"))).ToArray();
        var summary = new ExportSummary(entries.Length, entries.Count(row => row.Status == "InvalidDoi"),
            valid.Select(row => row.NormalizedDoi).Distinct().Count(), entries.Count(row => row.Status == "Held"),
            entries.Where(row => row.Status == "Held" && row.NormalizedDoi is not null).Select(row => row.NormalizedDoi).Distinct().Count(),
            entries.Count(row => row.DuplicateOfSourceRow is not null), eligible.Length, requests.Count, ready.Length,
            entries.Count(row => row.Status == "MetadataIncomplete"),
            entries.Count(row => row.Status is "MetadataError" or "MetadataUnavailable"),
            entries.Count(row => row.Status == "MetadataNotRequested"), maxMetadata, ready.Length == eligible.Length,
            entries.Count(row => row.Status == "MissingDoi"));
        var notices = new List<string>
        {
            "This export is scoped to one ComputingID's stored publication associations in the supported sources. It is not a complete career bibliography.",
            "Imported and synchronized associations retain their source attribution; they are not accepted OpenAlex matches. Existing current accepted OpenAlex decisions keep that status, but this tool makes no new authorship decision, import or database change.",
            "All three citation styles use exactly the same eligible CSL-JSON items, preserving provider author order. Held, invalid, duplicate and incomplete entries remain in the export but are not ordinary preview citations.",
            "Crossref is one metadata source. A missing or failed Crossref response does not establish that a publication or DOI is absent elsewhere."
        };
        if (!read.Snapshot.Complete) notices.Add("PARTIAL SOURCE READ: one or more source schemas or queries could not be read completely. Successfully read rows are retained; unavailable sources do not imply zero publications.");
        if (read.Snapshot.SourceChecks.Any(source => source.Status == "NotPresent")) notices.Add("Optional sources that are not installed are listed as NotPresent. Completion refers to the available supported sources, not universal publication-source coverage.");
        if (snapshotOnly) notices.Add("Snapshot-only mode: no external metadata requests were made.");
        if (!summary.MetadataCoverageComplete) notices.Add("Citation metadata coverage is partial. Inspect each publication's status; all stored DOI rows were still retained.");
        if (cancelled) notices.Add("Metadata enrichment was cancelled. Previously returned SQL source rows and their reported completeness status remain exported; unqueried entries are identified.");
        return new("OpenAlexIngestion.CitationPilot/1", startedUtc, DateTimeOffset.UtcNow, true, 0,
            "NotPerformed", "OneFacultyStoredPublicationAssociations", read.Faculty, read.Snapshot, summary,
            entries, ready.Select(row => row.Csl!.Value.Clone()).ToArray(), requests.ToArray(), notices.ToArray(),
            read.Rows.Where(row => !row.HasPublication).ToArray());
    }
}
