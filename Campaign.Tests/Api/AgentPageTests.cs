namespace Campaign.Tests.Api;

using System.Net;
using System.Net.Http.Json;
using Campaign.Api.Auth;

/// <summary>
/// What the single page form needs from the API: the page itself has to load without a token, and the
/// campaigns it offers have to be readable by the agent who will work in them.
/// </summary>
[Collection(nameof(ApiCollection))]
public class AgentPageTests
{
    private readonly ApiFixture _fixture;

    public AgentPageTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_page_is_served_from_the_api_itself_and_needs_no_token()
    {
        // Same origin as the API, which is why there is no CORS anywhere in this solution.
        var response = await _fixture.Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("Customer Reward Campaign", page, StringComparison.Ordinal);

        // The page talks to the same endpoints the tests do, with a bearer token.
        Assert.Contains("/api/v1/auth/token", page, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_agent_can_list_the_campaigns_the_form_offers()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await _fixture.Client.SendAsync(
            ApiFixture.Get("/api/v1/campaigns", world.AgentSubject));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var campaigns = await response.Content.ReadFromJsonAsync<List<CampaignBody>>() ?? [];

        var mine = Assert.Single(campaigns, campaign => campaign.Id == world.CampaignId);
        Assert.Equal("Active", mine.Status);
        Assert.Equal(5, mine.DailyLimitPerAgent);
        Assert.False(string.IsNullOrWhiteSpace(mine.Name));
    }

    [Fact]
    public async Task An_admin_can_list_them_too_because_the_results_table_needs_a_campaign()
    {
        await _fixture.NewWorldAsync();

        var response = await _fixture.Client.SendAsync(
            ApiFixture.Get("/api/v1/campaigns", "admin-1", CampaignRoles.Admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadFromJsonAsync<List<CampaignBody>>() ?? []);
    }

    [Fact]
    public async Task The_integration_account_has_no_business_listing_campaigns()
    {
        var response = await _fixture.Client.SendAsync(
            ApiFixture.Get("/api/v1/campaigns", "integration-1", CampaignRoles.Integration));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Listing_campaigns_without_a_token_is_refused()
    {
        var response = await _fixture.Client.SendAsync(ApiFixture.Get("/api/v1/campaigns", subject: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record CampaignBody(
        Guid Id,
        string Name,
        DateOnly StartDate,
        DateOnly EndDate,
        int DailyLimitPerAgent,
        decimal DiscountPercent,
        string Status);
}
