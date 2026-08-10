namespace Campaign.Api.Controllers;

using Campaign.Api.Auth;
using Campaign.Api.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>What a caller sends to the development login. Example: agent-1 / agent-1-password.</summary>
public sealed record TokenRequest(string Username, string Password);

/// <summary>A bearer token and when it stops being valid.</summary>
public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, string Role);

/// <summary>
/// A stand-in identity provider for the demo, and only that. The controller is removed from the
/// application model outside Development, so these routes do not exist there.
/// </summary>
[ApiController]
[DevelopmentOnly]
[AllowAnonymous]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    /// <summary>
    /// The seed accounts. Their passwords are written down in the README on purpose: they unlock
    /// nothing but a local demo, and this endpoint does not exist anywhere else.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Password, string Role)> Accounts =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["agent-1"] = ("agent-1-password", CampaignRoles.Agent),
            ["agent-2"] = ("agent-2-password", CampaignRoles.Agent),
            ["agent-3"] = ("agent-3-password", CampaignRoles.Agent),
            ["admin-1"] = ("admin-1-password", CampaignRoles.Admin),
            ["integration-1"] = ("integration-1-password", CampaignRoles.Integration)
        };

    private readonly DevelopmentTokenIssuer _issuer;

    public AuthController(DevelopmentTokenIssuer issuer)
    {
        _issuer = issuer;
    }

    /// <summary>Exchanges one of the seed accounts for a bearer token.</summary>
    [HttpPost("token")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<TokenResponse> Token([FromBody] TokenRequest request)
    {
        var username = request.Username ?? string.Empty;

        if (!Accounts.TryGetValue(username, out var account)
            || !string.Equals(account.Password, request.Password, StringComparison.Ordinal))
        {
            throw new ApiErrorException(ApiErrorCodes.Unauthenticated, "Unknown account or wrong password.");
        }

        var (token, expiresAt) = _issuer.Issue(username, account.Role);

        return Ok(new TokenResponse(token, "Bearer", expiresAt, account.Role));
    }
}
