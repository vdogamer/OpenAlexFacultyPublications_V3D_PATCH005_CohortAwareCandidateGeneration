namespace OpenAlex.Matching;

public sealed class MatchScoreResult
{
    public decimal Score { get; set; }
    public string CandidateStatus { get; set; } = "NeedsReview";
    public List<string> Evidence { get; set; } = new();

    public string EvidenceSummary => string.Join("; ", Evidence);
}
