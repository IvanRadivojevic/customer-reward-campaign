namespace Campaign.Tests.UseCases;

using Campaign.Core.Domain;
using Campaign.Core.UseCases;

public class ListGrantsTests
{
    [Fact]
    public async Task ListGrants_an_agent_sees_only_their_own_grants()
    {
        var context = await BuildTwoAgentsWithOneGrantEachAsync();

        var grants = await context.ListGrants.ExecuteAsync(
            new ListGrantsQuery(GrantTestContext.AgentUserId, ActorIsAdmin: false),
            CancellationToken.None);

        Assert.Single(grants);
        Assert.Equal(context.Agent.Id, grants[0].AgentId);
    }

    [Fact]
    public async Task ListGrants_an_admin_sees_the_grants_of_every_agent()
    {
        var context = await BuildTwoAgentsWithOneGrantEachAsync();

        var grants = await context.ListGrants.ExecuteAsync(
            new ListGrantsQuery(GrantTestContext.AdminUserId, ActorIsAdmin: true),
            CancellationToken.None);

        Assert.Equal(2, grants.Count);
    }

    [Fact]
    public async Task ListGrants_an_agent_asking_for_another_agent_is_rejected()
    {
        var context = await BuildTwoAgentsWithOneGrantEachAsync();

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.ListGrants.ExecuteAsync(
                new ListGrantsQuery(
                    GrantTestContext.AgentUserId,
                    ActorIsAdmin: false,
                    AgentId: context.OtherAgent.Id),
                CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ForbiddenAgentScope, error.Code);
    }

    [Fact]
    public async Task ListGrants_filters_by_status()
    {
        var context = await BuildTwoAgentsWithOneGrantEachAsync();
        var own = await context.ListGrants.ExecuteAsync(
            new ListGrantsQuery(GrantTestContext.AgentUserId, ActorIsAdmin: false),
            CancellationToken.None);

        await context.VoidGrant.ExecuteAsync(
            new VoidGrantCommand(own[0].Id, GrantTestContext.AgentUserId, ActorIsAdmin: false, "Mistake"),
            CancellationToken.None);

        var active = await context.ListGrants.ExecuteAsync(
            new ListGrantsQuery(GrantTestContext.AdminUserId, ActorIsAdmin: true, Status: GrantStatus.Active),
            CancellationToken.None);

        var voided = await context.ListGrants.ExecuteAsync(
            new ListGrantsQuery(GrantTestContext.AdminUserId, ActorIsAdmin: true, Status: GrantStatus.Voided),
            CancellationToken.None);

        Assert.Single(active);
        Assert.Single(voided);
    }

    [Fact]
    public async Task AgentNotActive_a_deactivated_agent_can_still_list_their_own_grants()
    {
        var context = await BuildTwoAgentsWithOneGrantEachAsync();
        context.DeactivateAgent();

        var grants = await context.ListGrants.ExecuteAsync(
            new ListGrantsQuery(GrantTestContext.AgentUserId, ActorIsAdmin: false),
            CancellationToken.None);

        Assert.Single(grants);
        Assert.Equal(context.Agent.Id, grants[0].AgentId);
    }

    [Fact]
    public async Task AgentNotActive_a_subject_that_is_not_an_agent_cannot_list_grants()
    {
        var context = await BuildTwoAgentsWithOneGrantEachAsync();

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.ListGrants.ExecuteAsync(
                new ListGrantsQuery("nobody-at-all", ActorIsAdmin: false),
                CancellationToken.None));

        Assert.Equal(DomainErrorCodes.AgentNotActive, error.Code);
    }

    private static async Task<GrantTestContext> BuildTwoAgentsWithOneGrantEachAsync()
    {
        var context = GrantTestContext.Build();
        await context.GrantAsync(customerExternalId: "1");
        await context.GrantAsync(customerExternalId: "2", agentUserId: GrantTestContext.OtherAgentUserId);
        return context;
    }
}
