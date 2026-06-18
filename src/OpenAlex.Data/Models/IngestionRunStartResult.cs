namespace OpenAlex.Data.Models;

public sealed class IngestionRunStartResult
{
    public long IngestionRunId { get; set; }
    public Guid RunCorrelationId { get; set; }
}
