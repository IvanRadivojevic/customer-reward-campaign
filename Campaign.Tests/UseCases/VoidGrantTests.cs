namespace Campaign.Tests.UseCases;

using Campaign.Core.Domain;
using Campaign.Core.UseCases;

public class VoidGrantTests
{
    [Fact]
    public async Task P04_voiding_records_the_reason_the_actor_and_the_time()
    {
        var context = GrantTestContext.Build();
        var granted = await context.GrantAsync(customerExternalId: "1");

        await context.VoidGrant.ExecuteAsync(
            new VoidGrantCommand(granted.Grant.Id, GrantTestContext.AgentUserId, ActorIsAdmin: false, "Wrong customer"),
            CancellationToken.None);

        var grant = granted.Grant;
        Assert.Equal(GrantStatus.Voided, grant.Status);
        Assert.Equal("Wrong customer", grant.VoidReason);
        Assert.Equal(GrantTestContext.AgentUserId, grant.VoidedByExternalUserId);
        Assert.Equal(context.Clock.UtcNow, grant.VoidedAtUtc);
    }

    [Fact]
    public async Task P04_voiding_frees_a_slot_on_the_same_business_date()
    {
        var context = GrantTestContext.Build(dailyLimitPerAgent: 1);
        var granted = await context.GrantAsync(customerExternalId: "1");

        await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.GrantAsync(customerExternalId: "2"));

        await context.VoidGrant.ExecuteAsync(
            new VoidGrantCommand(granted.Grant.Id, GrantTestContext.AgentUserId, ActorIsAdmin: false, "Mistake"),
            CancellationToken.None);

        var second = await context.GrantAsync(customerExternalId: "2");

        Assert.Equal(GrantStatus.Active, second.Grant.Status);
        Assert.Equal(2, context.Grants.Grants.Count);
    }

    [Fact]
    public async Task P04_a_voided_grant_no_longer_blocks_the_customer()
    {
        var context = GrantTestContext.Build();
        var granted = await context.GrantAsync(customerExternalId: "1");

        await context.VoidGrant.ExecuteAsync(
            new VoidGrantCommand(granted.Grant.Id, GrantTestContext.AgentUserId, ActorIsAdmin: false, "Mistake"),
            CancellationToken.None);

        var again = await context.GrantAsync(customerExternalId: "1");

        Assert.Equal(GrantStatus.Active, again.Grant.Status);
    }

    [Fact]
    public async Task P05_voiding_an_already_voided_grant_is_rejected()
    {
        var context = GrantTestContext.Build();
        var granted = await context.GrantAsync(customerExternalId: "1");
        var command = new VoidGrantCommand(
            granted.Grant.Id,
            GrantTestContext.AgentUserId,
            ActorIsAdmin: false,
            "Mistake");

        await context.VoidGrant.ExecuteAsync(command, CancellationToken.None);

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.VoidGrant.ExecuteAsync(command, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.GrantAlreadyVoided, error.Code);
    }

    [Fact]
    public async Task P05_the_grant_is_kept_and_its_business_fields_do_not_change()
    {
        var context = GrantTestContext.Build();
        var granted = await context.GrantAsync(customerExternalId: "1");
        var grant = granted.Grant;

        var customerExternalId = grant.CustomerExternalId;
        var customerNameAtGrant = grant.CustomerNameAtGrant;
        var businessDate = grant.BusinessDate;
        var grantedAtUtc = grant.GrantedAtUtc;
        var discountPercent = grant.DiscountPercent;

        context.Clock.UtcNow = context.Clock.UtcNow.AddHours(2);
        await context.VoidGrant.ExecuteAsync(
            new VoidGrantCommand(grant.Id, GrantTestContext.AgentUserId, ActorIsAdmin: false, "Mistake"),
            CancellationToken.None);

        Assert.Single(context.Grants.Grants);
        Assert.Equal(customerExternalId, grant.CustomerExternalId);
        Assert.Equal(customerNameAtGrant, grant.CustomerNameAtGrant);
        Assert.Equal(businessDate, grant.BusinessDate);
        Assert.Equal(grantedAtUtc, grant.GrantedAtUtc);
        Assert.Equal(discountPercent, grant.DiscountPercent);
    }

    [Fact]
    public async Task VoidGrant_an_agent_cannot_void_a_grant_of_another_agent()
    {
        var context = GrantTestContext.Build();
        var granted = await context.GrantAsync(customerExternalId: "1");

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.VoidGrant.ExecuteAsync(
                new VoidGrantCommand(
                    granted.Grant.Id,
                    GrantTestContext.OtherAgentUserId,
                    ActorIsAdmin: false,
                    "Not mine"),
                CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ForbiddenAgentScope, error.Code);
        Assert.Equal(GrantStatus.Active, granted.Grant.Status);
    }

    [Fact]
    public async Task VoidGrant_an_admin_can_void_a_grant_of_another_agent()
    {
        var context = GrantTestContext.Build();
        var granted = await context.GrantAsync(customerExternalId: "1");

        await context.VoidGrant.ExecuteAsync(
            new VoidGrantCommand(granted.Grant.Id, GrantTestContext.AdminUserId, ActorIsAdmin: true, "Audit"),
            CancellationToken.None);

        // The actor is stored as the subject claim, which is why an admin does not have to be an agent.
        Assert.Equal(GrantStatus.Voided, granted.Grant.Status);
        Assert.Equal(GrantTestContext.AdminUserId, granted.Grant.VoidedByExternalUserId);
    }

    [Fact]
    public async Task AgentNotActive_a_deactivated_agent_cannot_void_a_grant()
    {
        var context = GrantTestContext.Build();
        var granted = await context.GrantAsync(customerExternalId: "1");
        context.DeactivateAgent();

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.VoidGrant.ExecuteAsync(
                new VoidGrantCommand(granted.Grant.Id, GrantTestContext.AgentUserId, ActorIsAdmin: false, "Mistake"),
                CancellationToken.None));

        Assert.Equal(DomainErrorCodes.AgentNotActive, error.Code);
        Assert.Equal(GrantStatus.Active, granted.Grant.Status);
    }

    [Fact]
    public async Task AgentNotActive_a_subject_that_is_not_an_agent_cannot_void_a_grant()
    {
        var context = GrantTestContext.Build();
        var granted = await context.GrantAsync(customerExternalId: "1");

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.VoidGrant.ExecuteAsync(
                new VoidGrantCommand(granted.Grant.Id, "nobody-at-all", ActorIsAdmin: false, "Mistake"),
                CancellationToken.None));

        Assert.Equal(DomainErrorCodes.AgentNotActive, error.Code);
        Assert.Equal(GrantStatus.Active, granted.Grant.Status);
    }

    [Fact]
    public async Task AgentNotActive_an_admin_can_still_void_the_grant_of_a_deactivated_agent()
    {
        var context = GrantTestContext.Build();
        var granted = await context.GrantAsync(customerExternalId: "1");
        context.DeactivateAgent();

        await context.VoidGrant.ExecuteAsync(
            new VoidGrantCommand(granted.Grant.Id, GrantTestContext.AdminUserId, ActorIsAdmin: true, "Audit"),
            CancellationToken.None);

        Assert.Equal(GrantStatus.Voided, granted.Grant.Status);
    }

    [Fact]
    public async Task VoidGrant_reports_an_unknown_grant_as_not_found()
    {
        var context = GrantTestContext.Build();

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.VoidGrant.ExecuteAsync(
                new VoidGrantCommand(Guid.NewGuid(), GrantTestContext.AgentUserId, ActorIsAdmin: false, null),
                CancellationToken.None));

        Assert.Equal(DomainErrorCodes.GrantNotFound, error.Code);
    }

    [Fact]
    public async Task VoidGrant_rejects_a_reason_longer_than_the_column_allows()
    {
        var context = GrantTestContext.Build();
        var granted = await context.GrantAsync(customerExternalId: "1");

        var error = await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => context.VoidGrant.ExecuteAsync(
                new VoidGrantCommand(
                    granted.Grant.Id,
                    GrantTestContext.AgentUserId,
                    ActorIsAdmin: false,
                    new string('x', RewardGrant.MaxVoidReasonLength + 1)),
                CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ValidationFailed, error.Code);
        Assert.Equal(GrantStatus.Active, granted.Grant.Status);
    }
}
