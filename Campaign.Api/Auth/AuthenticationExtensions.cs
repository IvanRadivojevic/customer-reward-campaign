namespace Campaign.Api.Auth;

using System.Text;
using Campaign.Api.Errors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

public static class AuthenticationExtensions
{
    /// <summary>
    /// Development signs and validates with a symmetric key kept in User Secrets. Moving to Microsoft
    /// Entra ID means giving Authority and Audience in configuration instead of a signing key: the
    /// handler then fetches the issuer's public keys itself, and nothing in this file changes.
    /// </summary>
    public static IServiceCollection AddCampaignAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var authority = configuration["Auth:Authority"];

                // Claim names are kept exactly as the token spells them. Without this, ASP.NET Core
                // renames sub and role to long WS-Federation URIs and the policies stop matching.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Auth:Issuer"] ?? DevelopmentTokenIssuer.DefaultIssuer,
                    ValidAudience = configuration["Auth:Audience"] ?? DevelopmentTokenIssuer.DefaultAudience,
                    NameClaimType = ClaimsCallerContext.SubjectClaim,
                    RoleClaimType = ClaimsCallerContext.RoleClaim,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                if (string.IsNullOrWhiteSpace(authority))
                {
                    options.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey(configuration)));
                }
                else
                {
                    // A production identity provider: the keys come from the authority's metadata.
                    options.Authority = authority;
                }

                // Both answers have to look like every other refusal in the catalogue, so a client
                // parses one error shape and not three.
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await ProblemResponse.WriteAsync(
                            context.HttpContext,
                            ApiErrorCodes.Unauthenticated,
                            "The request carries no valid bearer token.");
                    },
                    // A policy or a role refused the caller. The narrower forbidden-agent-scope is
                    // raised by the use case that knows whose grant it is, not here.
                    OnForbidden = context => ProblemResponse.WriteAsync(
                        context.HttpContext,
                        ApiErrorCodes.Forbidden,
                        "This token is not allowed to use this endpoint.")
                };
            });

        return services;
    }

    /// <summary>
    /// Every endpoint requires a token unless it says otherwise, so a new controller is protected by
    /// default rather than by somebody remembering to protect it.
    /// </summary>
    public static IServiceCollection AddCampaignAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(CampaignPolicies.CanCreateGrant, policy =>
                policy.RequireRole(CampaignRoles.Agent));

            // An agent may void their own grant and an admin anybody's; which of the two this is gets
            // decided by the use case, because only it knows who owns the grant.
            options.AddPolicy(CampaignPolicies.CanVoidGrant, policy =>
                policy.RequireRole(CampaignRoles.Agent, CampaignRoles.Admin));

            options.AddPolicy(CampaignPolicies.CanReadCampaigns, policy =>
                policy.RequireRole(CampaignRoles.Agent, CampaignRoles.Admin));

            options.AddPolicy(CampaignPolicies.CanReadCustomers, policy =>
                policy.RequireRole(CampaignRoles.Agent, CampaignRoles.Admin));

            options.AddPolicy(CampaignPolicies.CanReadGrants, policy =>
                policy.RequireRole(CampaignRoles.Agent, CampaignRoles.Admin));

            options.AddPolicy(CampaignPolicies.CanImport, policy =>
                policy.RequireRole(CampaignRoles.Integration, CampaignRoles.Admin));

            options.AddPolicy(CampaignPolicies.CanViewReports, policy =>
                policy.RequireRole(CampaignRoles.Admin, CampaignRoles.Integration));
        });

        return services;
    }

    public static string SigningKey(IConfiguration configuration) =>
        configuration["Auth:SigningKey"]
        ?? throw new InvalidOperationException(
            "Auth:SigningKey is not configured. See appsettings.Example.json; in development it belongs in User Secrets.");
}
