namespace Campaign.Api.Errors;

using Campaign.Core.Domain;

/// <summary>
/// The error catalogue as the API publishes it. The domain owns the codes it raises; the three here
/// belong to the web layer, and this file is the one place that decides which status each carries.
/// </summary>
public static class ApiErrorCodes
{
    /// <summary>Produced by authentication, never by a use case: the token is missing or invalid.</summary>
    public const string Unauthenticated = "unauthenticated";

    public const string CsvInvalid = "csv-invalid";

    public const string DirectoryUnavailable = "directory-unavailable";

    /// <summary>How long a caller should wait before asking the catalogue again.</summary>
    public static readonly TimeSpan DirectoryRetryAfter = TimeSpan.FromSeconds(30);

    public static int StatusFor(string code) => code switch
    {
        DomainErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        CsvInvalid => StatusCodes.Status400BadRequest,
        Unauthenticated => StatusCodes.Status401Unauthorized,
        DomainErrorCodes.ForbiddenAgentScope => StatusCodes.Status403Forbidden,
        DomainErrorCodes.AgentNotActive => StatusCodes.Status403Forbidden,
        DomainErrorCodes.CustomerNotFound => StatusCodes.Status404NotFound,
        DomainErrorCodes.GrantNotFound => StatusCodes.Status404NotFound,
        DomainErrorCodes.CampaignNotActive => StatusCodes.Status409Conflict,
        DomainErrorCodes.DailyLimitReached => StatusCodes.Status409Conflict,
        DomainErrorCodes.CustomerAlreadyRewarded => StatusCodes.Status409Conflict,
        DomainErrorCodes.GrantAlreadyVoided => StatusCodes.Status409Conflict,
        DomainErrorCodes.IdempotencyKeyReused => StatusCodes.Status409Conflict,
        DirectoryUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };
}

/// <summary>An error the web layer raises itself, carrying a code from the same catalogue.</summary>
public sealed class ApiErrorException : Exception
{
    public ApiErrorException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
