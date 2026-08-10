namespace Campaign.Tests.Api;

using System.Net.Http.Json;
using Campaign.Api.Auth;
using Campaign.Core.Domain;
using Campaign.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// One hosted application shared by the API tests. Every test makes its own campaign and its own
/// agent, so tests stay independent of each other and of whatever the seed wrote.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
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

    public async Task<int> CountGrantsAsync(Guid campaignId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.RewardGrants.CountAsync(grant => grant.CampaignId == campaignId);
    }

    public static HttpRequestMessage GrantRequest(
        Guid campaignId,
        string customerExternalId,
        string subject,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/campaigns/{campaignId}/grants")
        {
            Content = JsonContent.Create(new { customerExternalId })
        };

        request.Headers.Add(DevelopmentHeaderCallerContext.SubjectHeader, subject);
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return request;
    }

    public static HttpRequestMessage Get(string url, string subject, bool asAdmin = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(DevelopmentHeaderCallerContext.SubjectHeader, subject);

        if (asAdmin)
        {
            request.Headers.Add(DevelopmentHeaderCallerContext.RoleHeader, "admin");
        }

        return request;
    }

    public static HttpRequestMessage Post(string url, string subject, object? body = null, bool asAdmin = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body ?? new { })
        };

        request.Headers.Add(DevelopmentHeaderCallerContext.SubjectHeader, subject);

        if (asAdmin)
        {
            request.Headers.Add(DevelopmentHeaderCallerContext.RoleHeader, "admin");
        }

        return request;
    }

    /// <summary>The machine readable error type from an RFC 7807 body.</summary>
    public static async Task<string?> ProblemTypeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Type;
}

public sealed record TestWorld(Guid CampaignId, Guid AgentId, string AgentSubject, DateOnly BusinessDate);

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
