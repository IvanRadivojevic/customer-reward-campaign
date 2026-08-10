namespace Campaign.Api.Auth;

/// <summary>
/// The three roles the token can carry. <see cref="Integration"/> is a system account for the CSV
/// import, not a person, which is why it never appears on the endpoints an agent works with.
/// </summary>
public static class CampaignRoles
{
    public const string Agent = "agent";
    public const string Admin = "admin";
    public const string Integration = "integration";
}

/// <summary>
/// Authorisation lives in named policies rather than in role checks scattered over the controllers,
/// so who may do what is decided in one file and read in one place.
/// </summary>
public static class CampaignPolicies
{
    public const string CanCreateGrant = nameof(CanCreateGrant);
    public const string CanVoidGrant = nameof(CanVoidGrant);
    public const string CanImport = nameof(CanImport);
    public const string CanViewReports = nameof(CanViewReports);
    public const string CanReadCampaigns = nameof(CanReadCampaigns);
    public const string CanReadCustomers = nameof(CanReadCustomers);
    public const string CanReadGrants = nameof(CanReadGrants);
}
