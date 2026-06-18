namespace OpenAlex.OperationalLogging;

public sealed class OperationalLoggingOptions
{
    public string SourceApplication { get; set; } = "OpenAlexFacultyPublications";
    public string EnvironmentName { get; set; } = "LocalTest";
    public bool EnableDatabaseLogging { get; set; } = true;
}
