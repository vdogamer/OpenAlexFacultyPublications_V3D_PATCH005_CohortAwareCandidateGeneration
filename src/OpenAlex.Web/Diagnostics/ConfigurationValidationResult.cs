namespace OpenAlex.Web.Diagnostics;

public sealed class ConfigurationValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool IsReady => Errors.Count == 0;
}
