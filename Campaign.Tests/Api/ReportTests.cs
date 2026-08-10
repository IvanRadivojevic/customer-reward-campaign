namespace Campaign.Tests.Api;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Campaign.Api.Auth;
using Campaign.Core.Domain;
using Campaign.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The campaign report, read out of the vw_CampaignResults view. Every number here is also counted by
/// hand in the test, because a report nobody checked against a hand count is a report nobody trusts.
/// </summary>
[Collection(nameof(ApiCollection))]
public class ReportTests
{
    private const string Admin = "admin-1";

    private readonly string _integration = $"integration-{Guid.NewGuid():N}";
    private readonly ApiFixture _fixture;

    public ReportTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task One_rewarded_customer_with_three_purchases_converts_one_grant_and_stays_below_one_hundred_percent()
    {
        var world = await _fixture.NewWorldAsync();

        // Two customers are rewarded; only the first one buys, and buys three times.
        await GrantTo(world, "1");
        await GrantTo(world, "2");

        await UploadAsync(world.CampaignId, """
            CustomerId,PurchaseDate,Amount,Currency
            1,2026-09-14,149.90,EUR
            1,2026-09-21,19.99,EUR
            1,2026-10-02,75.40,EUR
            """);

        var report = await ReadResultsAsync(world.CampaignId, "agent");

        // Three purchases, one converted grant: without an order identifier the file cannot say
        // whether a repeated customer is a repeated purchase, so the rows are counted separately and
        // the grant only once.
        Assert.Equal(1, report.Totals.ConvertedGrants);
        Assert.Equal(3, report.Totals.MatchedRows);
        Assert.Equal(2, report.Totals.ActiveGrants);

        Assert.Equal(0.5m, report.Totals.ConversionRate);
        Assert.True(report.Totals.ConversionRate < 1m, "Conversion can never pass 100%.");
    }

    [Fact]
    public async Task The_numbers_match_a_hand_count_of_the_scenario()
    {
        var world = await _fixture.NewWorldAsync();

        // By hand: four grants are made, one of them is voided before the file arrives, so three
        // stay active. The file brings three purchases for customer 1, one for customer 2, one for
        // the customer whose grant was voided, and one for somebody nobody rewarded.
        await GrantTo(world, "1");
        await GrantTo(world, "2");
        await GrantTo(world, "3");
        var voided = await GrantTo(world, "4");
        await VoidAsync(voided, world.AgentSubject);

        await UploadAsync(world.CampaignId, """
            CustomerId,PurchaseDate,Amount,Currency
            1,2026-09-14,149.90,EUR
            1,2026-09-21,19.99,EUR
            1,2026-10-02,75.40,EUR
            2,2026-09-15,89.00,EUR
            4,2026-09-16,42.00,EUR
            9999,2026-09-17,12.00,EUR
            """);

        var report = await ReadResultsAsync(world.CampaignId, "agent");

        Assert.Equal(3, report.Totals.ActiveGrants);
        Assert.Equal(1, report.Totals.VoidedGrants);

        // Customers 1 and 2 converted; customer 3 never bought.
        Assert.Equal(2, report.Totals.ConvertedGrants);

        // Three rows for customer 1 plus one for customer 2. The rows for the voided grant and for
        // the unknown customer are unmatched and count here for nothing.
        Assert.Equal(4, report.Totals.MatchedRows);

        // Two of three active grants converted, and matchedRows being larger than activeGrants does
        // not push it over 100%.
        Assert.Equal(0.6667m, report.Totals.ConversionRate);

        // One agent made all of them, so the single group repeats the totals.
        var row = Assert.Single(report.Rows);
        Assert.Equal(world.AgentSubject, row.Key);
        Assert.Equal(report.Totals.ActiveGrants, row.ActiveGrants);
        Assert.Equal(report.Totals.VoidedGrants, row.VoidedGrants);
        Assert.Equal(report.Totals.ConvertedGrants, row.ConvertedGrants);
        Assert.Equal(report.Totals.MatchedRows, row.MatchedRows);
    }

    [Fact]
    public async Task Voiding_a_grant_after_the_import_leaves_the_conversion_but_keeps_the_matched_row()
    {
        var world = await _fixture.NewWorldAsync();
        await GrantTo(world, "1");
        var second = await GrantTo(world, "2");

        await UploadAsync(world.CampaignId, """
            CustomerId,PurchaseDate,Amount,Currency
            1,2026-09-14,10.00,EUR
            2,2026-09-15,20.00,EUR
            """);

        var before = await ReadResultsAsync(world.CampaignId, "agent");
        Assert.Equal(2, before.Totals.ActiveGrants);
        Assert.Equal(2, before.Totals.ConvertedGrants);
        Assert.Equal(2, before.Totals.MatchedRows);

        await VoidAsync(second, world.AgentSubject);

        var after = await ReadResultsAsync(world.CampaignId, "agent");

        // P-09: a voided grant is in neither the numerator nor the denominator. The purchase it was
        // matched to still happened, though, so matchedRows keeps counting it - the two figures
        // answer different questions, which is why the specification reports them separately.
        Assert.Equal(1, after.Totals.ActiveGrants);
        Assert.Equal(1, after.Totals.VoidedGrants);
        Assert.Equal(1, after.Totals.ConvertedGrants);
        Assert.Equal(2, after.Totals.MatchedRows);
        Assert.Equal(1m, after.Totals.ConversionRate);
    }

    [Fact]
    public async Task Grouping_by_day_splits_the_same_totals_across_business_dates()
    {
        var world = await _fixture.NewWorldAsync();
        await GrantTo(world, "1");

        // A grant written straight into the database on an earlier business date, because the clock
        // the API reads cannot be moved from a test.
        var yesterday = world.BusinessDate.AddDays(-1);
        await AddGrantAsync(world, "2", yesterday);

        var byDay = await ReadResultsAsync(world.CampaignId, "day");
        var byAgent = await ReadResultsAsync(world.CampaignId, "agent");

        Assert.Equal(2, byDay.Rows.Count);
        Assert.Contains(byDay.Rows, row => row.Key == world.BusinessDate.ToString("yyyy-MM-dd"));
        Assert.Contains(byDay.Rows, row => row.Key == yesterday.ToString("yyyy-MM-dd"));

        // Same population, different grouping: the totals cannot differ.
        Assert.Equal(byAgent.Totals.ActiveGrants, byDay.Totals.ActiveGrants);
        Assert.Equal(2, byDay.Totals.ActiveGrants);
        Assert.Equal(byDay.Totals.ActiveGrants, byDay.Rows.Sum(row => row.ActiveGrants));
    }

    [Fact]
    public async Task Unmatched_purchases_are_listed_rather_than_dropped()
    {
        var world = await _fixture.NewWorldAsync();
        await GrantTo(world, "1");

        await UploadAsync(world.CampaignId, """
            CustomerId,PurchaseDate,Amount,Currency
            1,2026-09-14,10.00,EUR
            9998,2026-09-15,20.00,EUR
            9999,2026-09-16,30.00,EUR
            """);

        var response = await _fixture.Client.SendAsync(ApiFixture.Get(
            $"/api/v1/campaigns/{world.CampaignId}/unmatched-purchases",
            Admin,
            CampaignRoles.Admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await response.Content.ReadFromJsonAsync<List<UnmatchedRow>>() ?? [];

        Assert.Equal(2, rows.Count);
        Assert.Equal(["9998", "9999"], rows.Select(row => row.CustomerExternalId).Order());
        Assert.All(rows, row => Assert.Equal(nameof(MatchStatus.Unmatched), row.MatchStatus));
    }

    [Fact]
    public async Task An_unknown_grouping_is_refused()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await _fixture.Client.SendAsync(ApiFixture.Get(
            $"/api/v1/campaigns/{world.CampaignId}/results?groupBy=month",
            Admin,
            CampaignRoles.Admin));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DomainErrorCodes.ValidationFailed, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task An_agent_may_not_read_the_report()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await _fixture.Client.SendAsync(ApiFixture.Get(
            $"/api/v1/campaigns/{world.CampaignId}/results?groupBy=agent",
            world.AgentSubject));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_integration_account_may_read_the_report()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await _fixture.Client.SendAsync(ApiFixture.Get(
            $"/api/v1/campaigns/{world.CampaignId}/results?groupBy=agent",
            _integration,
            CampaignRoles.Integration));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<ReportBody> ReadResultsAsync(Guid campaignId, string groupBy)
    {
        var response = await _fixture.Client.SendAsync(ApiFixture.Get(
            $"/api/v1/campaigns/{campaignId}/results?groupBy={groupBy}",
            Admin,
            CampaignRoles.Admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<ReportBody>()
            ?? throw new InvalidOperationException("The report returned no body.");
    }

    private async Task<Guid> GrantTo(TestWorld world, string customerExternalId)
    {
        var response = await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            world.CampaignId,
            customerExternalId,
            world.AgentSubject,
            Guid.NewGuid().ToString()));

        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("id").GetGuid();
    }

    private async Task VoidAsync(Guid grantId, string subject) =>
        await _fixture.Client.SendAsync(
            ApiFixture.Post($"/api/v1/grants/{grantId}/void", subject, new { reason = "test" }));

    private async Task AddGrantAsync(TestWorld world, string customerExternalId, DateOnly businessDate)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        db.RewardGrants.Add(RewardGrant.Create(
            Guid.NewGuid(),
            world.CampaignId,
            world.AgentId,
            customerExternalId,
            $"Customer {customerExternalId}",
            businessDate,
            clock.GetUtcNow(),
            discountPercent: 10m,
            Guid.NewGuid().ToString()));

        await db.SaveChangesAsync();
    }

    private async Task UploadAsync(Guid campaignId, string csv)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "purchases.csv");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/campaigns/{campaignId}/imports")
        {
            Content = content
        };

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiFixture.TokenFor(_integration, CampaignRoles.Integration));

        var response = await _fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record ReportBody(Guid CampaignId, TotalsBody Totals, IReadOnlyList<RowBody> Rows);

    private sealed record TotalsBody(
        int ActiveGrants,
        int VoidedGrants,
        int ConvertedGrants,
        int MatchedRows,
        decimal ConversionRate);

    private sealed record RowBody(
        string Key,
        string DisplayName,
        int ActiveGrants,
        int VoidedGrants,
        int ConvertedGrants,
        int MatchedRows,
        decimal ConversionRate);

    private sealed record UnmatchedRow(string? CustomerExternalId, string MatchStatus);
}
