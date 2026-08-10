namespace Campaign.Api.Auth;

using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Issues the tokens the development login hands out. It exists so the demo can be driven from a
/// browser without an identity provider; in every other environment the controller that uses it is
/// not even registered.
/// </summary>
public sealed class DevelopmentTokenIssuer
{
    public const string DefaultIssuer = "campaign-api";
    public const string DefaultAudience = "campaign-api";

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public DevelopmentTokenIssuer(IConfiguration configuration, TimeProvider timeProvider)
    {
        _configuration = configuration;
        _timeProvider = timeProvider;
    }

    public (string Token, DateTimeOffset ExpiresAtUtc) Issue(string subject, string role)
    {
        var expiresAt = _timeProvider.GetUtcNow().Add(Lifetime);
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(AuthenticationExtensions.SigningKey(_configuration)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _configuration["Auth:Issuer"] ?? DefaultIssuer,
            Audience = _configuration["Auth:Audience"] ?? DefaultAudience,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimsCallerContext.SubjectClaim, subject),
                new Claim(ClaimsCallerContext.RoleClaim, role)
            ]),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }
}
