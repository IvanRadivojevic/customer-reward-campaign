namespace Campaign.Tests.Api;

using System.Net;
using System.Net.Http.Json;
using Campaign.Api.Auth;
using Campaign.Api.Errors;
using Campaign.Core.Domain;

/// <summary>
/// Who may call what, and what a refusal looks like. Every case here is one of the acceptance
/// criteria for the authentication work package.
/// </summary>
[Collection(nameof(ApiCollection))]
public class AuthenticationTests
{
    private readonly ApiFixture _fixture;

    public AuthenticationTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_request_without_a_token_is_unauthenticated_and_says_so_in_the_documented_shape()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await _fixture.Client.SendAsync(
            ApiFixture.Get($"/api/v1/agents/me/quota?campaignId={world.CampaignId}", subject: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ApiErrorCodes.Unauthenticated, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task A_token_signed_with_the_wrong_key_is_unauthenticated()
    {
        var world = await _fixture.NewWorldAsync();

        var request = ApiFixture.Get($"/api/v1/agents/me/quota?campaignId={world.CampaignId}", subject: null);
        request.Headers.Add("Authorization", "Bearer not-a-real-token");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiErrorCodes.Unauthenticated, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task An_agent_cannot_void_the_grant_of_another_agent()
    {
        var owner = await _fixture.NewWorldAsync();
        var stranger = await _fixture.NewWorldAsync();

        var created = await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            owner.CampaignId, "1", owner.AgentSubject, Guid.NewGuid().ToString()));
        var grantId = await GrantIdAsync(created);

        var response = await _fixture.Client.SendAsync(ApiFixture.Post(
            $"/api/v1/grants/{grantId}/void",
            stranger.AgentSubject,
            new { reason = "not mine" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(DomainErrorCodes.ForbiddenAgentScope, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task An_admin_can_void_the_grant_of_any_agent()
    {
        var owner = await _fixture.NewWorldAsync();

        var created = await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            owner.CampaignId, "2", owner.AgentSubject, Guid.NewGuid().ToString()));
        var grantId = await GrantIdAsync(created);

        var response = await _fixture.Client.SendAsync(ApiFixture.Post(
            $"/api/v1/grants/{grantId}/void",
            "admin-1",
            new { reason = "audit" },
            CampaignRoles.Admin));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task The_integration_account_may_not_reach_the_endpoints_an_agent_works_with()
    {
        // Refused by the role, not by ownership, so the generic type is the honest one.
        var response = await _fixture.Client.SendAsync(
            ApiFixture.Get("/api/v1/grants", "integration-1", CampaignRoles.Integration));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ApiErrorCodes.Forbidden, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task An_admin_may_not_create_a_grant_because_only_agents_award_them()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            world.CampaignId, "1", "admin-1", Guid.NewGuid().ToString(), CampaignRoles.Admin));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ApiErrorCodes.Forbidden, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task The_two_kinds_of_403_are_told_apart_by_their_type()
    {
        // Same status, different reason: one is about the role, the other about whose grant it is.
        var owner = await _fixture.NewWorldAsync();
        var stranger = await _fixture.NewWorldAsync();

        var created = await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            owner.CampaignId, "3", owner.AgentSubject, Guid.NewGuid().ToString()));
        var grantId = await GrantIdAsync(created);

        var byRole = await _fixture.Client.SendAsync(
            ApiFixture.Get("/api/v1/grants", "integration-1", CampaignRoles.Integration));

        var byOwnership = await _fixture.Client.SendAsync(ApiFixture.Post(
            $"/api/v1/grants/{grantId}/void",
            stranger.AgentSubject,
            new { reason = "not mine" }));

        Assert.Equal(HttpStatusCode.Forbidden, byRole.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byOwnership.StatusCode);

        var roleType = await ApiFixture.ProblemTypeAsync(byRole);
        var ownershipType = await ApiFixture.ProblemTypeAsync(byOwnership);

        Assert.Equal(ApiErrorCodes.Forbidden, roleType);
        Assert.Equal(DomainErrorCodes.ForbiddenAgentScope, ownershipType);
        Assert.NotEqual(roleType, ownershipType);
    }

    [Fact]
    public async Task Health_answers_without_a_token()
    {
        var response = await _fixture.Client.SendAsync(ApiFixture.Get("/health", subject: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_development_login_hands_out_a_token_that_the_api_accepts()
    {
        await _fixture.EnsureAgentAsync("agent-1");

        var login = await _fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/token",
            new { username = "agent-1", password = "agent-1-password" });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var token = await login.Content.ReadFromJsonAsync<TokenBody>();
        Assert.NotNull(token);
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal(CampaignRoles.Agent, token.Role);

        // agent-1 is a seed account, so the token is enough to read that agent's own grants.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/grants");
        request.Headers.Add("Authorization", $"Bearer {token.AccessToken}");

        Assert.Equal(HttpStatusCode.OK, (await _fixture.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task The_development_login_refuses_a_wrong_password()
    {
        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/token",
            new { username = "agent-1", password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiErrorCodes.Unauthenticated, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task Outside_development_the_token_endpoint_does_not_exist()
    {
        using var production = CampaignApiFactory.AsProduction();
        using var client = production.CreateClient();

        // The request carries a valid admin token, so nothing can refuse it for lack of one. What
        // comes back is 404: the route is gone, not merely closed.
        var request = ApiFixture.Post(
            "/api/v1/auth/token",
            "admin-1",
            new { username = "agent-1", password = "agent-1-password" },
            CampaignRoles.Admin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // And in Development the very same call is answered.
        var development = await _fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/token",
            new { username = "agent-1", password = "agent-1-password" });

        Assert.Equal(HttpStatusCode.OK, development.StatusCode);
    }

    private static async Task<Guid> GrantIdAsync(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private sealed record TokenBody(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, string Role);
}
