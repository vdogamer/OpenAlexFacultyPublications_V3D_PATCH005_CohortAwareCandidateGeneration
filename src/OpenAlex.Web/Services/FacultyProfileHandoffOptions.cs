namespace OpenAlex.Web.Services;

public sealed class FacultyProfileHandoffOptions
{
    public bool Enabled { get; set; } = true;

    public string PublicationsEditBaseUrl { get; set; } =
        "https://apps.med.virginia.edu/ResearchFacultyDirectory/Profile/Edit/HTML.aspx";

    // PATCH018E intentionally leaves direct write-back disabled until the
    // Research Faculty Directory exposes a supported update API/endpoint.
    public bool DirectPublishingEnabled { get; set; } = false;
}
