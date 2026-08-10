namespace Campaign.Tests.Api;

using System.Net;
using System.Net.Http.Json;
using Campaign.Api.Errors;
using Campaign.Api.Middleware;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// The limit is counted per token, so a test gets its own budget by using its own agent. That is also
/// the property the rule is for: one busy agent must not spend the budget of the next.
/// </summary>
[Collection(nameof(ApiCollection))]
public class RateLimitingTests
{
    private const int RequestsPerMinute = 100;

    private readonly ApiFixture _fixture;

    public RateLimitingTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_hundred_and_first_request_in_a_minute_is_refused()
    {
        var world = await _fixture.NewWorldAsync();

        for (var request = 1; request <= RequestsPerMinute; request++)
        {
            var allowed = await _fixture.Client.SendAsync(
                ApiFixture.Get("/api/v1/grants", world.AgentSubject));

            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var refused = await _fixture.Client.SendAsync(
            ApiFixture.Get("/api/v1/grants", world.AgentSubject));

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(60, refused.Headers.RetryAfter?.Delta?.TotalSeconds);
    }

    [Fact]
    public async Task A_refusal_by_the_limiter_looks_like_every_other_refusal()
    {
        var world = await _fixture.NewWorldAsync();

        for (var request = 1; request <= RequestsPerMinute; request++)
        {
            await _fixture.Client.SendAsync(ApiFixture.Get("/api/v1/grants", world.AgentSubject));
        }

        var refused = await _fixture.Client.SendAsync(
            ApiFixture.Get("/api/v1/grants", world.AgentSubject));

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

        // The whole shape, not only the status: the same media type, the same body and the same
        // correlation header a client gets from any other refusal.
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        Assert.Equal(60, refused.Headers.RetryAfter?.Delta?.TotalSeconds);
        Assert.True(refused.Headers.Contains(CorrelationIdMiddleware.HeaderName));

        var problem = await refused.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(ApiErrorCodes.RateLimitExceeded, problem.Type);
        Assert.Equal(ApiErrorCodes.RateLimitExceeded, problem.Title);
        Assert.Equal((int)HttpStatusCode.TooManyRequests, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.Equal("/api/v1/grants", problem.Instance);
        Assert.True(problem.Extensions.ContainsKey("correlationId"));
    }

    [Fact]
    public async Task The_correlation_id_of_a_refusal_is_the_one_the_caller_sent()
    {
        var world = await _fixture.NewWorldAsync();

        for (var request = 1; request <= RequestsPerMinute; request++)
        {
            await _fixture.Client.SendAsync(ApiFixture.Get("/api/v1/grants", world.AgentSubject));
        }

        var last = ApiFixture.Get("/api/v1/grants", world.AgentSubject);
        last.Headers.Add(CorrelationIdMiddleware.HeaderName, "given-by-the-caller");
        var refused = await _fixture.Client.SendAsync(last);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(
            "given-by-the-caller",
            refused.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task One_agent_spending_the_budget_does_not_refuse_another()
    {
        var busy = await _fixture.NewWorldAsync();
        var quiet = await _fixture.NewWorldAsync();

        for (var request = 1; request <= RequestsPerMinute + 1; request++)
        {
            await _fixture.Client.SendAsync(ApiFixture.Get("/api/v1/grants", busy.AgentSubject));
        }

        var response = await _fixture.Client.SendAsync(ApiFixture.Get("/api/v1/grants", quiet.AgentSubject));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
