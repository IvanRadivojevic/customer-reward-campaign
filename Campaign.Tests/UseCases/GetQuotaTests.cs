namespace Campaign.Tests.UseCases;

using Campaign.Core.Domain;
using Campaign.Core.UseCases;

public class GetQuotaTests
{
    [Fact]
    public async Task P02_quota_reports_used_and_limit_for_the_current_business_date()
    {
        var context = GrantTestContext.Build(dailyLimitPerAgent: 5);
        await context.GrantAsync(customerExternalId: "1");
        await context.GrantAsync(customerExternalId: "2");

        var quota = await context.GetQuota.ExecuteAsync(
            new GetQuotaQuery(context.Campaign.Id, GrantTestContext.AgentUserId),
            CancellationToken.None);

        Assert.Equal(2, quota.Used);
        Assert.Equal(5, quota.Limit);
        Assert.Equal(GrantTestContext.DefaultBusinessDate, quota.BusinessDate);
        Assert.Equal(context.Agent.Id, quota.AgentId);
    }

    [Fact]
    public async Task P02_quota_counts_only_the_grants_of_the_agent_who_asks()
    {
        var context = GrantTestContext.Build();
        await context.GrantAsync(customerExternalId: "1");
        await context.GrantAsync(customerExternalId: "2", agentUserId: GrantTestContext.OtherAgentUserId);

        var quota = await context.GetQuota.ExecuteAsync(
            new GetQuotaQuery(context.Campaign.Id, GrantTestContext.OtherAgentUserId),
            CancellationToken.None);

        Assert.Equal(1, quota.Used);
    }

    [Fact]
    public async Task AgentNotActive_a_deactivated_agent_can_still_read_the_quota()
    {
        var context = GrantTestContext.Build();
        await context.GrantAsync(customerExternalId: "1");
        context.DeactivateAgent();

        var quota = await context.GetQuota.ExecuteAsync(
            new GetQuotaQuery(context.Campaign.Id, GrantTestContext.AgentUserId),
            CancellationToken.None);

        Assert.Equal(1, quota.Used);
    }

    [Fact]
    public async Task AgentNotActive_a_subject_that_is_not_an_agent_cannot_read_a_quota()
    {
        var context = GrantTestContext.Build();

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.GetQuota.ExecuteAsync(
                new GetQuotaQuery(context.Campaign.Id, "nobody-at-all"),
                CancellationToken.None));

        Assert.Equal(DomainErrorCodes.AgentNotActive, error.Code);
    }

    [Fact]
    public async Task P04_a_voided_grant_gives_the_slot_back_to_the_quota()
    {
        var context = GrantTestContext.Build();
        var granted = await context.GrantAsync(customerExternalId: "1");

        await context.VoidGrant.ExecuteAsync(
            new VoidGrantCommand(granted.Grant.Id, GrantTestContext.AgentUserId, ActorIsAdmin: false, "Mistake"),
            CancellationToken.None);

        var quota = await context.GetQuota.ExecuteAsync(
            new GetQuotaQuery(context.Campaign.Id, GrantTestContext.AgentUserId),
            CancellationToken.None);

        Assert.Equal(0, quota.Used);
    }
}
