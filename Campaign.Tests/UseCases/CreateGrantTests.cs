namespace Campaign.Tests.UseCases;

using Campaign.Core.Domain;

public class CreateGrantTests
{
    [Fact]
    public async Task P01_grant_is_created_while_the_campaign_is_active_and_the_date_is_inside_the_window()
    {
        var context = GrantTestContext.Build();

        var result = await context.GrantAsync(customerExternalId: "1");

        Assert.False(result.Replayed);
        Assert.Equal(GrantStatus.Active, result.Grant.Status);
        Assert.Equal(GrantTestContext.DefaultBusinessDate, result.Grant.BusinessDate);
        Assert.Single(context.Grants.Grants);
    }

    [Fact]
    public async Task P01_grant_is_created_on_the_first_day_of_the_campaign()
    {
        var context = GrantTestContext.Build(
            startDate: GrantTestContext.DefaultBusinessDate,
            endDate: GrantTestContext.DefaultBusinessDate.AddDays(5));

        var result = await context.GrantAsync();

        Assert.Equal(GrantTestContext.DefaultBusinessDate, result.Grant.BusinessDate);
    }

    [Fact]
    public async Task P01_grant_is_created_on_the_last_day_of_the_campaign()
    {
        var context = GrantTestContext.Build(
            startDate: GrantTestContext.DefaultBusinessDate.AddDays(-5),
            endDate: GrantTestContext.DefaultBusinessDate);

        var result = await context.GrantAsync();

        Assert.Equal(GrantTestContext.DefaultBusinessDate, result.Grant.BusinessDate);
    }

    [Fact]
    public async Task P01_grant_is_rejected_the_day_after_the_campaign_ended()
    {
        var context = GrantTestContext.Build(
            startDate: GrantTestContext.DefaultBusinessDate.AddDays(-5),
            endDate: GrantTestContext.DefaultBusinessDate.AddDays(-1));

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(() => context.GrantAsync());

        Assert.Equal(DomainErrorCodes.CampaignNotActive, error.Code);
        Assert.Empty(context.Grants.Grants);
    }

    [Fact]
    public async Task P01_grant_is_rejected_the_day_before_the_campaign_starts()
    {
        var context = GrantTestContext.Build(
            startDate: GrantTestContext.DefaultBusinessDate.AddDays(1),
            endDate: GrantTestContext.DefaultBusinessDate.AddDays(5));

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(() => context.GrantAsync());

        Assert.Equal(DomainErrorCodes.CampaignNotActive, error.Code);
    }

    [Theory]
    [InlineData(CampaignStatus.Draft)]
    [InlineData(CampaignStatus.Closed)]
    public async Task P01_grant_is_rejected_when_the_campaign_is_not_active(CampaignStatus status)
    {
        var context = GrantTestContext.Build(status: status);

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(() => context.GrantAsync());

        Assert.Equal(DomainErrorCodes.CampaignNotActive, error.Code);
    }

    [Fact]
    public async Task P01_business_date_follows_the_configured_time_zone_and_not_utc()
    {
        // 22:30 UTC is already 00:30 of the next day in Belgrade, and the campaign lasts that one
        // local day only. Reading the clock as UTC would reject this grant.
        var context = GrantTestContext.Build(
            now: new DateTimeOffset(2026, 8, 9, 22, 30, 0, TimeSpan.Zero),
            startDate: new DateOnly(2026, 8, 10),
            endDate: new DateOnly(2026, 8, 10));

        var result = await context.GrantAsync();

        Assert.Equal(new DateOnly(2026, 8, 10), result.Grant.BusinessDate);
    }

    [Fact]
    public async Task P02_grant_is_rejected_when_the_daily_limit_is_reached()
    {
        var context = GrantTestContext.Build(dailyLimitPerAgent: 5);

        for (var customer = 1; customer <= 5; customer++)
        {
            await context.GrantAsync(customerExternalId: customer.ToString());
        }

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.GrantAsync(customerExternalId: "6"));

        Assert.Equal(DomainErrorCodes.DailyLimitReached, error.Code);
        Assert.Equal(5, error.Details["used"]);
        Assert.Equal(5, error.Details["limit"]);
        Assert.Equal(5, context.Grants.Grants.Count);
    }

    [Fact]
    public async Task P02_the_limit_is_the_one_configured_on_the_campaign()
    {
        var context = GrantTestContext.Build(dailyLimitPerAgent: 2);

        await context.GrantAsync(customerExternalId: "1");
        await context.GrantAsync(customerExternalId: "2");

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.GrantAsync(customerExternalId: "3"));

        Assert.Equal(DomainErrorCodes.DailyLimitReached, error.Code);
        Assert.Equal(2, error.Details["limit"]);
    }

    [Fact]
    public async Task P02_the_limit_counts_per_business_date_so_the_next_day_starts_over()
    {
        var context = GrantTestContext.Build(dailyLimitPerAgent: 2);

        await context.GrantAsync(customerExternalId: "1");
        await context.GrantAsync(customerExternalId: "2");

        context.Clock.UtcNow = context.Clock.UtcNow.AddDays(1);
        var result = await context.GrantAsync(customerExternalId: "3");

        Assert.Equal(GrantTestContext.DefaultBusinessDate.AddDays(1), result.Grant.BusinessDate);
    }

    [Fact]
    public async Task P02_the_limit_is_counted_per_agent()
    {
        var context = GrantTestContext.Build(dailyLimitPerAgent: 1);

        await context.GrantAsync(customerExternalId: "1");

        var result = await context.GrantAsync(
            customerExternalId: "2",
            agentUserId: GrantTestContext.OtherAgentUserId);

        Assert.Equal(context.OtherAgent.Id, result.Grant.AgentId);
    }

    [Fact]
    public async Task P03_a_second_active_grant_for_the_same_customer_is_rejected()
    {
        var context = GrantTestContext.Build();
        var first = await context.GrantAsync(customerExternalId: "1");

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.GrantAsync(customerExternalId: "1"));

        Assert.Equal(DomainErrorCodes.CustomerAlreadyRewarded, error.Code);
        Assert.Equal(first.Grant.Id, error.Details["grantId"]);
        Assert.Single(context.Grants.Grants);
    }

    [Fact]
    public async Task P03_the_rule_holds_across_agents()
    {
        var context = GrantTestContext.Build();
        await context.GrantAsync(customerExternalId: "1");

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.GrantAsync(customerExternalId: "1", agentUserId: GrantTestContext.OtherAgentUserId));

        Assert.Equal(DomainErrorCodes.CustomerAlreadyRewarded, error.Code);
    }

    [Fact]
    public async Task P06_the_same_key_for_the_same_request_returns_the_existing_grant()
    {
        var context = GrantTestContext.Build();
        const string key = "5f9d1e6c-1f2a-4a6f-9f47-1b3a2c4d5e6f";

        var first = await context.GrantAsync(customerExternalId: "1", idempotencyKey: key);
        var replay = await context.GrantAsync(customerExternalId: "1", idempotencyKey: key);

        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Grant.Id, replay.Grant.Id);
        Assert.Single(context.Grants.Grants);
    }

    [Fact]
    public async Task P06_the_same_key_for_a_different_customer_is_rejected()
    {
        var context = GrantTestContext.Build();
        const string key = "5f9d1e6c-1f2a-4a6f-9f47-1b3a2c4d5e6f";

        await context.GrantAsync(customerExternalId: "1", idempotencyKey: key);

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.GrantAsync(customerExternalId: "2", idempotencyKey: key));

        Assert.Equal(DomainErrorCodes.IdempotencyKeyReused, error.Code);
        Assert.Single(context.Grants.Grants);
    }

    [Fact]
    public async Task P06_the_same_key_for_a_different_campaign_is_rejected()
    {
        var context = GrantTestContext.Build();
        const string key = "5f9d1e6c-1f2a-4a6f-9f47-1b3a2c4d5e6f";

        var otherCampaign = Campaign.Create(
            Guid.NewGuid(),
            "Second campaign",
            GrantTestContext.DefaultBusinessDate,
            GrantTestContext.DefaultBusinessDate,
            15m,
            CampaignStatus.Active);
        context.Grants.WithCampaign(otherCampaign);

        await context.GrantAsync(customerExternalId: "1", idempotencyKey: key);

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.GrantAsync(customerExternalId: "1", idempotencyKey: key, campaignId: otherCampaign.Id));

        Assert.Equal(DomainErrorCodes.IdempotencyKeyReused, error.Code);
    }

    [Fact]
    public async Task P06_the_same_key_used_by_another_agent_is_a_different_request()
    {
        var context = GrantTestContext.Build();
        const string key = "5f9d1e6c-1f2a-4a6f-9f47-1b3a2c4d5e6f";

        await context.GrantAsync(customerExternalId: "1", idempotencyKey: key);
        var second = await context.GrantAsync(
            customerExternalId: "2",
            idempotencyKey: key,
            agentUserId: GrantTestContext.OtherAgentUserId);

        Assert.False(second.Replayed);
        Assert.Equal(2, context.Grants.Grants.Count);
    }

    [Fact]
    public async Task P07_the_customer_name_is_frozen_at_the_moment_of_the_grant()
    {
        var context = GrantTestContext.Build();

        var result = await context.GrantAsync(customerExternalId: "1");
        context.Directory.Rename("1", "Renamed In The Catalogue");

        Assert.Equal("Customer 1", result.Grant.CustomerNameAtGrant);
    }

    [Fact]
    public async Task P07_the_discount_is_copied_from_the_campaign_and_a_later_change_does_not_move_it()
    {
        var context = GrantTestContext.Build(discountPercent: 10m);

        var result = await context.GrantAsync(customerExternalId: "1");

        // The campaign is changed afterwards, the way an administrator would change it.
        context.Grants.WithCampaign(Campaign.Create(
            context.Campaign.Id,
            context.Campaign.Name,
            context.Campaign.StartDate,
            context.Campaign.EndDate,
            25m,
            CampaignStatus.Active,
            context.Campaign.DailyLimitPerAgent));

        Assert.Equal(10m, result.Grant.DiscountPercent);
    }

    [Fact]
    public async Task CreateGrant_is_rejected_when_the_catalogue_does_not_know_the_customer()
    {
        var context = GrantTestContext.Build();

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.GrantAsync(customerExternalId: "does-not-exist"));

        Assert.Equal(DomainErrorCodes.CustomerNotFound, error.Code);
        Assert.Empty(context.Grants.Grants);
    }

    [Fact]
    public async Task AgentNotActive_a_deactivated_agent_cannot_create_a_grant()
    {
        var context = GrantTestContext.Build();
        context.DeactivateAgent();

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(() => context.GrantAsync());

        Assert.Equal(DomainErrorCodes.AgentNotActive, error.Code);
        Assert.Empty(context.Grants.Grants);
    }

    [Fact]
    public async Task AgentNotActive_a_subject_that_is_not_an_agent_cannot_create_a_grant()
    {
        var context = GrantTestContext.Build();

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.GrantAsync(agentUserId: "nobody-at-all"));

        Assert.Equal(DomainErrorCodes.AgentNotActive, error.Code);
        Assert.Empty(context.Grants.Grants);
    }

    [Fact]
    public async Task AgentNotActive_a_replay_is_still_answered_after_the_agent_was_deactivated()
    {
        // Answering a replay creates nothing, so it stays a read and remains allowed.
        var context = GrantTestContext.Build();
        const string key = "5f9d1e6c-1f2a-4a6f-9f47-1b3a2c4d5e6f";
        var first = await context.GrantAsync(customerExternalId: "1", idempotencyKey: key);

        context.DeactivateAgent();
        var replay = await context.GrantAsync(customerExternalId: "1", idempotencyKey: key);

        Assert.True(replay.Replayed);
        Assert.Equal(first.Grant.Id, replay.Grant.Id);
        Assert.Single(context.Grants.Grants);
    }

    [Fact]
    public async Task CreateGrant_is_rejected_without_an_idempotency_key()
    {
        var context = GrantTestContext.Build();

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.GrantAsync(idempotencyKey: "  "));

        Assert.Equal(DomainErrorCodes.ValidationFailed, error.Code);
    }
}
