namespace Campaign.Tests.Api;

using System.Net;
using Campaign.Api.Errors;
using Campaign.Core.Domain;

/// <summary>
/// Walks the error catalogue from SPEC section 7 and reaches each entry through the API, checking
/// both the status and the machine readable type. An entry nobody can reach is a promise the
/// documentation makes and the code does not keep.
/// </summary>
[Collection(nameof(ApiCollection))]
public class ErrorCatalogueTests
{
    private readonly ApiFixture _fixture;

    public ErrorCatalogueTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Validation_failed_when_the_idempotency_key_is_missing()
    {
        var world = await _fixture.NewWorldAsync();

        var request = ApiFixture.Post(
            $"/api/v1/campaigns/{world.CampaignId}/grants",
            world.AgentSubject,
            new { customerExternalId = "1" });

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DomainErrorCodes.ValidationFailed, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task Customer_not_found_for_an_id_the_catalogue_does_not_know()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            world.CampaignId,
            "no-such-customer",
            world.AgentSubject,
            Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(DomainErrorCodes.CustomerNotFound, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task Grant_not_found_when_voiding_something_that_never_existed()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await _fixture.Client.SendAsync(
            ApiFixture.Post($"/api/v1/grants/{Guid.NewGuid()}/void", world.AgentSubject, new { reason = "typo" }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(DomainErrorCodes.GrantNotFound, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task Campaign_not_active_for_a_campaign_that_does_not_exist()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            Guid.NewGuid(),
            "1",
            world.AgentSubject,
            Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(DomainErrorCodes.CampaignNotActive, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task Daily_limit_reached_carries_used_and_limit_in_the_body()
    {
        var world = await _fixture.NewWorldAsync(dailyLimitPerAgent: 1);

        await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            world.CampaignId, "1", world.AgentSubject, Guid.NewGuid().ToString()));

        var response = await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            world.CampaignId, "2", world.AgentSubject, Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(DomainErrorCodes.DailyLimitReached, await ApiFixture.ProblemTypeAsync(response));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"used\":1", body, StringComparison.Ordinal);
        Assert.Contains("\"limit\":1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Customer_already_rewarded_names_the_grant_that_holds_the_customer()
    {
        var world = await _fixture.NewWorldAsync();

        await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            world.CampaignId, "3", world.AgentSubject, Guid.NewGuid().ToString()));

        var response = await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            world.CampaignId, "3", world.AgentSubject, Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(DomainErrorCodes.CustomerAlreadyRewarded, await ApiFixture.ProblemTypeAsync(response));
        Assert.Contains("grantId", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grant_already_voided_when_the_same_grant_is_voided_twice()
    {
        var world = await _fixture.NewWorldAsync();

        var created = await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            world.CampaignId, "4", world.AgentSubject, Guid.NewGuid().ToString()));
        var grantId = await GrantIdAsync(created);

        var first = await _fixture.Client.SendAsync(
            ApiFixture.Post($"/api/v1/grants/{grantId}/void", world.AgentSubject, new { reason = "mistake" }));
        var second = await _fixture.Client.SendAsync(
            ApiFixture.Post($"/api/v1/grants/{grantId}/void", world.AgentSubject, new { reason = "again" }));

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(DomainErrorCodes.GrantAlreadyVoided, await ApiFixture.ProblemTypeAsync(second));
    }

    [Fact]
    public async Task Idempotency_key_reused_for_a_different_customer()
    {
        var world = await _fixture.NewWorldAsync();
        var key = Guid.NewGuid().ToString();

        await _fixture.Client.SendAsync(ApiFixture.GrantRequest(world.CampaignId, "5", world.AgentSubject, key));

        var response = await _fixture.Client.SendAsync(
            ApiFixture.GrantRequest(world.CampaignId, "6", world.AgentSubject, key));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(DomainErrorCodes.IdempotencyKeyReused, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task Agent_not_active_for_a_subject_that_is_not_an_agent()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            world.CampaignId, "1", "nobody-at-all", Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(DomainErrorCodes.AgentNotActive, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task Directory_unavailable_answers_503_with_retry_after()
    {
        // The catalogue is replaced in process, so this test says nothing about the network.
        using var factory = CampaignApiFactory.WithBrokenDirectory();
        using var client = factory.CreateClient();
        var world = await _fixture.NewWorldAsync();

        var response = await client.SendAsync(ApiFixture.GrantRequest(
            world.CampaignId, "1", world.AgentSubject, Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ApiErrorCodes.DirectoryUnavailable, await ApiFixture.ProblemTypeAsync(response));
        Assert.Equal(30, response.Headers.RetryAfter?.Delta?.TotalSeconds);
    }

    [Fact]
    public async Task Every_response_carries_a_correlation_id_and_echoes_the_one_it_was_given()
    {
        var world = await _fixture.NewWorldAsync();

        var generated = await _fixture.Client.SendAsync(ApiFixture.Get("/health", world.AgentSubject));
        Assert.True(generated.Headers.Contains("X-Correlation-Id"));

        var mine = ApiFixture.Get("/health", world.AgentSubject);
        mine.Headers.Add("X-Correlation-Id", "given-by-the-caller");
        var echoed = await _fixture.Client.SendAsync(mine);

        Assert.Equal("given-by-the-caller", echoed.Headers.GetValues("X-Correlation-Id").Single());
    }

    private static async Task<Guid> GrantIdAsync(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }
}
