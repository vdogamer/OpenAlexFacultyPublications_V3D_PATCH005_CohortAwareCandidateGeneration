namespace OpenAlex.Data.Models;

public sealed class FacultyManualReviewRule
{
    public long FacultyId { get; set; }
    public bool RequireVerifiedSeedForLikelyStatus { get; set; }
    public bool UseManualReviewForAbbreviatedSearch { get; set; }
    public bool RequireAuthorAffiliationSupport { get; set; }
    public bool RequireCollaborationOrTopicSupport { get; set; }
    public string? Reason { get; set; }
}
