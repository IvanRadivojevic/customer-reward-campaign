namespace Campaign.Tests.Api;

using System.Net;
using Campaign.Api.Controllers;
using Campaign.Core.Domain;

/// <summary>
/// The rules that only mean anything under load. Every test here fires at least ten requests at the
/// same time; a sequential version of the same test would pass against a broken implementation.
/// </summary>
[Collection(nameof(ApiCollection))]
public class ParallelGrantTests
{
    private const int Concurrency = 12;

    private readonly ApiFixture _fixture;

    public ParallelGrantTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task P02_parallel_requests_for_one_agent_stop_exactly_at_the_daily_limit()
    {
        const int limit = 5;
        var world = await _fixture.NewWorldAsync(limit);

        // Twelve different customers, so nothing but the daily limit can refuse them.
        var responses = await WhenAll(Enumerable.Range(1, Concurrency).Select(customer =>
            ApiFixture.GrantRequest(
                world.CampaignId,
                customer.ToString(),
                world.AgentSubject,
                Guid.NewGuid().ToString())));

        var created = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
        var refused = await CountProblemsAsync(responses, DomainErrorCodes.DailyLimitReached);

        Assert.True(limit == created, await DescribeAsync(responses));
        Assert.Equal(Concurrency - limit, refused);
        Assert.Equal(limit, await _fixture.CountGrantsAsync(world.CampaignId));
    }

    [Fact]
    public async Task P03_parallel_requests_for_one_customer_produce_exactly_one_grant()
    {
        var world = await _fixture.NewWorldAsync();

        // The same customer every time, but a different key, so these are genuinely different
        // requests racing for the same customer.
        var responses = await WhenAll(Enumerable.Range(0, Concurrency).Select(_ =>
            ApiFixture.GrantRequest(world.CampaignId, "1", world.AgentSubject, Guid.NewGuid().ToString())));

        var created = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
        var refused = await CountProblemsAsync(responses, DomainErrorCodes.CustomerAlreadyRewarded);

        Assert.Equal(1, created);
        Assert.Equal(Concurrency - 1, refused);
        Assert.Equal(1, await _fixture.CountGrantsAsync(world.CampaignId));
    }

    [Fact]
    public async Task P06_parallel_identical_requests_create_one_grant_and_nothing_fails()
    {
        var world = await _fixture.NewWorldAsync();
        var key = Guid.NewGuid().ToString();

        var responses = await WhenAll(Enumerable.Range(0, Concurrency).Select(_ =>
            ApiFixture.GrantRequest(world.CampaignId, "1", world.AgentSubject, key)));

        var created = responses.Where(response => response.StatusCode == HttpStatusCode.Created).ToList();
        var replayed = responses.Where(response => response.StatusCode == HttpStatusCode.OK).ToList();

        // This is the one rule where no request may fail: a repeated key is an answer, not an error.
        Assert.True(created.Count == 1, await DescribeAsync(responses));
        Assert.True(replayed.Count == Concurrency - 1, await DescribeAsync(responses));
        Assert.All(replayed, response =>
            Assert.Equal("true", response.Headers.GetValues(GrantsController.ReplayedHeader).Single()));
        Assert.Equal(1, await _fixture.CountGrantsAsync(world.CampaignId));
    }

    private async Task<IReadOnlyList<HttpResponseMessage>> WhenAll(IEnumerable<HttpRequestMessage> requests) =>
        await Task.WhenAll(requests.Select(request => _fixture.Client.SendAsync(request)));

    /// <summary>Every answer, so a failing run says what actually came back instead of only a count.</summary>
    private static async Task<string> DescribeAsync(IEnumerable<HttpResponseMessage> responses)
    {
        var lines = new List<string>();

        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            lines.Add($"{(int)response.StatusCode} {response.StatusCode}: {Shorten(body)}");
        }

        return string.Join(Environment.NewLine, lines.OrderBy(line => line, StringComparer.Ordinal));
    }

    private static string Shorten(string body) =>
        body.Length <= 200 ? body : body[..200];

    private static async Task<int> CountProblemsAsync(
        IEnumerable<HttpResponseMessage> responses,
        string expectedType)
    {
        var matches = 0;

        foreach (var response in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
        {
            if (await ApiFixture.ProblemTypeAsync(response) == expectedType)
            {
                matches++;
            }
        }

        return matches;
    }
}
