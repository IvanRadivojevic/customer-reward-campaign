namespace Campaign.Api.Auth;

using System.Security.Claims;
using Campaign.Api.Errors;

/// <summary>
/// The caller as the validated token describes them. The subject claim is what a grant records as
/// its owner, and what a void records as the actor - which is why an admin can void somebody else's
/// grant without being a row in the Agent table.
/// </summary>
public sealed class ClaimsCallerContext : ICallerContext
{
    public const string SubjectClaim = "sub";
    public const string RoleClaim = "role";

    private readonly IHttpContextAccessor _accessor;

    public ClaimsCallerContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string ExternalUserId
    {
        get
        {
            var subject = _accessor.HttpContext?.User.FindFirstValue(SubjectClaim);

            return string.IsNullOrWhiteSpace(subject)
                ? throw new ApiErrorException(
                    ApiErrorCodes.Unauthenticated,
                    "The token carries no subject.")
                : subject;
        }
    }

    public bool IsAdmin => _accessor.HttpContext?.User.IsInRole(CampaignRoles.Admin) ?? false;
}
