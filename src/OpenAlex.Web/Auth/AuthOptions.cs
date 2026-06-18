namespace OpenAlex.Web.Auth;

public sealed class AuthOptions
{
    public string Mode { get; set; } = "DemoCookie";
    public bool AllowAutoProvisionUsers { get; set; } = false;
    public string DefaultRoleForNewUsers { get; set; } = "Reader";
    public bool RequireEnabledUser { get; set; } = true;
    public ShibbolethHeaderOptions ShibbolethHeaders { get; set; } = new();
}

public sealed class ShibbolethHeaderOptions
{
    public string UserNameHeader { get; set; } = "eppn";
    public string ComputingIdHeader { get; set; } = "uid";
    public string DisplayNameHeader { get; set; } = "displayName";
    public string EmailHeader { get; set; } = "mail";
    public string AffiliationHeader { get; set; } = "affiliation";
    public string EntitlementHeader { get; set; } = "entitlement";
    public string GroupsHeader { get; set; } = "memberOf";
    public string GroupsDelimiter { get; set; } = ";";
}
