namespace OpenAlex.Data.Models;

public sealed class AcceptedIdentityBridgeExecutionSafetyCounts
{
    public int AuthorCandidateRows { get; set; }
    public int AuthorDecisionRows { get; set; }
    public int PublicationCandidateRows { get; set; }
    public int PublicationDecisionRows { get; set; }
}

public sealed class AcceptedIdentityBridgeExecutionResult
{
    public int SelectedIdentityCount { get; set; }
    public int CandidateRowsInserted { get; set; }
    public int CandidateRowsUpdated { get; set; }
    public int DecisionRowsInserted { get; set; }
    public int DecisionsAlreadyAccepted { get; set; }
    public int MissingLocalAuthorCount { get; set; }
}
