namespace OpenAlex.Data.Models;

public sealed class PublicationEvidenceEnhancementResult
{
    public bool Skipped { get; set; }
    public string? SkipReason { get; set; }
    public int CandidateRowsEvaluated { get; set; }
    public int CandidateRowsEnhanced { get; set; }
    public int RowsWithAffiliatedInstitutionEvidence { get; set; }
    public int RowsWithKnownCoauthorEvidence { get; set; }
}
