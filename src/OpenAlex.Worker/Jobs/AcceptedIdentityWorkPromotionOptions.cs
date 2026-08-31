namespace OpenAlex.Worker.Jobs;

public sealed class AcceptedIdentityWorkPromotionOptions
{
    public string CohortName { get; set; } = "V3E_PATCH017B8_FIVE_FACULTY";
    public int ExpectedFacultyCount { get; set; } = 5;
    public int ExpectedMissingWorkCount { get; set; } = 158;
    public int MaxWorks { get; set; } = 200;
    public bool ConfirmPromotion { get; set; }
    public string StartedBy { get; set; } = "OpenAlex.V3E.PATCH017B10A";
    public string ApplicationVersion { get; set; } = "3E-PATCH017B10A-WorkPromotion";
}
