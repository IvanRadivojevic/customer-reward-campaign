namespace Campaign.Api.Auth;

using Campaign.Api.Errors;

/// <summary>
/// A stand-in for authentication until JWT bearer arrives. It takes the caller's identity from two
/// request headers, which means anybody could claim to be anybody - so it is only ever registered in
/// Development, and outside Development every endpoint answers 401 instead.
/// </summary>
public sealed class DevelopmentHeaderCallerContext : ICallerContext
{
    public const string SubjectHeader = "X-Dev-Subject";
    public const string RoleHeader = "X-Dev-Role";

    private readonly IHttpContextAccessor _accessor;

    public DevelopmentHeaderCallerContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string ExternalUserId
    {
        get
        {
            var subject = _accessor.HttpContext?.Request.Headers[SubjectHeader].ToString();

            return string.IsNullOrWhiteSpace(subject)
                ? throw new ApiErrorException(
                    ApiErrorCodes.Unauthenticated,
                    $"Development builds identify the caller with the {SubjectHeader} header.")
                : subject;
        }
    }

    public bool IsAdmin =>
        string.Equals(
            _accessor.HttpContext?.Request.Headers[RoleHeader].ToString(),
            "admin",
            StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What runs outside Development until the real authentication exists: nobody is authenticated.
/// </summary>
public sealed class UnauthenticatedCallerContext : ICallerContext
{
    public string ExternalUserId => throw new ApiErrorException(
        ApiErrorCodes.Unauthenticated,
        "This build has no authentication configured yet.");

    public bool IsAdmin => false;
}
