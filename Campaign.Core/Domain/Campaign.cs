namespace Campaign.Core.Domain;

public sealed class Campaign
{
    public const int DefaultDailyLimitPerAgent = 5;

    private Campaign(
        Guid id,
        string name,
        DateOnly startDate,
        DateOnly endDate,
        int dailyLimitPerAgent,
        decimal discountPercent,
        CampaignStatus status)
    {
        Id = id;
        Name = name;
        StartDate = startDate;
        EndDate = endDate;
        DailyLimitPerAgent = dailyLimitPerAgent;
        DiscountPercent = discountPercent;
        Status = status;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    /// <summary>The daily limit lives on the campaign, so it is configurable per campaign.</summary>
    public int DailyLimitPerAgent { get; private set; }

    public decimal DiscountPercent { get; private set; }

    public CampaignStatus Status { get; private set; }

    public static Campaign Create(
        Guid id,
        string name,
        DateOnly startDate,
        DateOnly endDate,
        decimal discountPercent,
        CampaignStatus status,
        int dailyLimitPerAgent = DefaultDailyLimitPerAgent)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException(DomainErrorCodes.ValidationFailed, "Campaign name is required.");
        }

        if (endDate < startDate)
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "Campaign end date cannot be earlier than its start date.");
        }

        if (dailyLimitPerAgent < 1)
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "Daily limit per agent must be at least one.");
        }

        if (discountPercent is < 0 or > 100)
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "Discount percent must be between 0 and 100.");
        }

        return new Campaign(id, name, startDate, endDate, dailyLimitPerAgent, discountPercent, status);
    }

    /// <summary>
    /// P-01: a reward can be granted only while the campaign is active and the business date falls
    /// inside [StartDate, EndDate], both ends included.
    /// </summary>
    public bool IsOpenOn(DateOnly businessDate) =>
        Status == CampaignStatus.Active && businessDate >= StartDate && businessDate <= EndDate;
}
