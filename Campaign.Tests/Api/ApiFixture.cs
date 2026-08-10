namespace Campaign.Tests.Api;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Campaign.Api.Auth;
using Campaign.Core.Domain;
using Campaign.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// One hosted application shared by the API tests. Every test makes its own campaign and its own
/// agent, so tests stay independent of each other and of whatever the seed wrote.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    /// <summary>Even here the clock is reached through TimeProvider, never directly.</summary>
    private static readonly TimeProvider Clock = TimeProvider.System;

    public CampaignApiFactory Factory { get; private set; } = null!;

    public HttpClient Client { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Factory = new CampaignApiFactory();
        Client = Factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
        Factory?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>A campaign that is open today and an agent nobody else in the test run shares.</summary>
    public async Task<TestWorld> NewWorldAsync(int dailyLimitPerAgent = 5)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var businessDates = scope.ServiceProvider.GetRequiredService<BusinessDateProvider>();

        var today = businessDates.Today();
        var campaign = Campaign.Create(
            Guid.NewGuid(),
            "Integration test campaign",
            today.AddDays(-1),
            today.AddDays(1),
            discountPercent: 10m,
            CampaignStatus.Active,
            dailyLimitPerAgent);

        var subject = $"agent-{Guid.NewGuid():N}";
        var agent = Agent.Create(Guid.NewGuid(), subject, "Integration test agent");

        db.Campaigns.Add(campaign);
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        return new TestWorld(campaign.Id, agent.Id, subject, today);
    }

    /// <summary>
    /// Makes sure a seed subject really is an agent in this database. The seed only writes into an
    /// empty database, and by the time these tests run it is not empty any more.
    /// </summary>
    public async Task EnsureAgentAsync(string subject)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.Agents.AnyAsync(agent => agent.ExternalUserId == subject))
        {
            return;
        }

        db.Agents.Add(Agent.Create(Guid.NewGuid(), subject, subject));
        await db.SaveChangesAsync();
    }

    public async Task<int> CountGrantsAsync(Guid campaignId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.RewardGrants.CountAsync(grant => grant.CampaignId == campaignId);
    }

    /// <summary>
    /// Mints a token the application will accept, signed with the same key the test host is
    /// configured with. Tests make their own agents, so they cannot use the seed accounts.
    /// </summary>
    public static string TokenFor(string subject, string role = CampaignRoles.Agent)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CampaignApiFactory.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = CampaignApiFactory.Issuer,
            Audience = CampaignApiFactory.Audience,
            Expires = Clock.GetUtcNow().AddMinutes(30).UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimsCallerContext.SubjectClaim, subject),
                new Claim(ClaimsCallerContext.RoleClaim, role)
            ]),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public static HttpRequestMessage GrantRequest(
        Guid campaignId,
        string customerExternalId,
        string subject,
        string idempotencyKey,
        string role = CampaignRoles.Agent)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/campaigns/{campaignId}/grants")
        {
            Content = JsonContent.Create(new { customerExternalId })
        };

        Authorize(request, subject, role);
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return request;
    }

    public static HttpRequestMessage Get(string url, string? subject, string role = CampaignRoles.Agent)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        Authorize(request, subject, role);

        return request;
    }

    public static HttpRequestMessage Post(
        string url,
        string? subject,
        object? body = null,
        string role = CampaignRoles.Agent)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body ?? new { })
        };

        Authorize(request, subject, role);

        return request;
    }

    /// <summary>A null subject means no token at all, which is how the 401 case is reached.</summary>
    private static void Authorize(HttpRequestMessage request, string? subject, string role)
    {
        if (subject is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(subject, role));
        }
    }

    /// <summary>The machine readable error type from an RFC 7807 body.</summary>
    public static async Task<string?> ProblemTypeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Type;
}

public sealed record TestWorld(Guid CampaignId, Guid AgentId, string AgentSubject, DateOnly BusinessDate);

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
