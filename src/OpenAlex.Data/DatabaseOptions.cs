namespace OpenAlex.Data;

public sealed class DatabaseOptions
{
    // Preferred V2 setting.
    public string ConnectionString { get; set; } = string.Empty;

    // Backward-compatible fallback for ASP.NET Core ConnectionStrings.
    public string DefaultConnectionName { get; set; } = "DefaultConnection";
}
