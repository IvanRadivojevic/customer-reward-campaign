namespace Campaign.Tests.UseCases;

using Campaign.Core.Domain;
using Campaign.Core.UseCases;
using Campaign.Tests.Fakes;

/// <summary>
/// Wires the four grant use cases to in-memory ports. Every test starts from one active campaign,
/// two agents and a small catalogue, and changes only the one thing it is about.
/// </summary>
internal sealed class GrantTestContext
{
    public const string TimeZoneId = "Europe/Belgrade";
    public const string AgentUserId = "agent-1";
    public const string OtherAgentUserId = "agent-2";
    public const string AdminUserId = "admin-1";

    /// <summary>12:00 in Belgrade, so the default business date is unambiguous.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly DefaultBusinessDate = new(2026, 8, 9);

    private GrantTestContext(
        Campaign campaign,
        Agent agent,
        Agent otherAgent,
        FakeGrantRepository grants,
        FakeCustomerDirectory directory,
        FixedTimeProvider clock,
        BusinessDateProvider businessDates)
    {
        Campaign = campaign;
        Agent = agent;
        OtherAgent = otherAgent;
        Grants = grants;
        Directory = directory;
        Clock = clock;
        BusinessDates = businessDates;
    }

    public Campaign Campaign { get; }

    public Agent Agent { get; }

    public Agent OtherAgent { get; }

    public FakeGrantRepository Grants { get; }

    public FakeCustomerDirectory Directory { get; }

    public FixedTimeProvider Clock { get; }

    public BusinessDateProvider BusinessDates { get; }

    public CreateGrant CreateGrant => new(Grants, Directory, new FakeUnitOfWork(Grants), BusinessDates);

    public VoidGrant VoidGrant => new(Grants, BusinessDates);

    public GetQuota GetQuota => new(Grants, BusinessDates);

    public ListGrants ListGrants => new(Grants);

    public static GrantTestContext Build(
        DateTimeOffset? now = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CampaignStatus status = CampaignStatus.Active,
        int dailyLimitPerAgent = 5,
        decimal discountPercent = 10m)
    {
        var clock = new FixedTimeProvider(now ?? DefaultNow);
        var businessDates = new BusinessDateProvider(clock, TimeZoneId);

        var campaign = Campaign.Create(
            Guid.NewGuid(),
            "Loyal customers, August 2026",
            startDate ?? new DateOnly(2026, 8, 6),
            endDate ?? new DateOnly(2026, 8, 12),
            discountPercent,
            status,
            dailyLimitPerAgent);

        var agent = Agent.Create(Guid.NewGuid(), AgentUserId, "Marko M.");
        var otherAgent = Agent.Create(Guid.NewGuid(), OtherAgentUserId, "Jelena J.");

        var grants = new FakeGrantRepository()
            .WithCampaign(campaign)
            .WithAgent(agent)
            .WithAgent(otherAgent);

        var directory = new FakeCustomerDirectory();
        for (var i = 1; i <= 8; i++)
        {
            directory.With(i.ToString(), $"Customer {i}");
        }

        return new GrantTestContext(campaign, agent, otherAgent, grants, directory, clock, businessDates);
    }

    /// <summary>
    /// Replaces the agent with a deactivated one carrying the same identity, the way an
    /// administrator would switch the flag off between two requests.
    /// </summary>
    public void DeactivateAgent()
    {
        Grants.WithAgent(Agent.Create(Agent.Id, Agent.ExternalUserId, Agent.DisplayName, isActive: false));
    }

    public Task<CreateGrantResult> GrantAsync(
        string customerExternalId = "1",
        string? idempotencyKey = null,
        string agentUserId = AgentUserId,
        Guid? campaignId = null) =>
        CreateGrant.ExecuteAsync(
            new CreateGrantCommand(
                campaignId ?? Campaign.Id,
                customerExternalId,
                agentUserId,
                idempotencyKey ?? Guid.NewGuid().ToString()),
            CancellationToken.None);
}
