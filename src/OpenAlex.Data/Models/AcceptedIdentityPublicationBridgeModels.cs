namespace OpenAlex.Data.Models;

public sealed class AcceptedIdentityPublicationBridgeResult
{
    public bool Skipped { get; set; }
    public string? SkipReason { get; set; }
    public int AcceptedIdentityRows { get; set; }
    public int AuthorCandidateRowsInserted { get; set; }
    public int AuthorCandidateRowsUpdated { get; set; }
    public int AuthorDecisionRowsInserted { get; set; }
    public int ExistingAcceptedAuthorDecisionsSkipped { get; set; }
}
