namespace Campaign.Core.Domain;

/// <summary>
/// One discount awarded to one customer by one agent. A grant is never deleted and its business
/// fields are never edited (P-05); a correction is a void followed by a new grant.
/// </summary>
public sealed class RewardGrant
{
    public const int MaxVoidReasonLength = 500;

    private RewardGrant(
        Guid id,
        Guid campaignId,
        Guid agentId,
        string customerExternalId,
        string customerNameAtGrant,
        DateOnly businessDate,
        DateTimeOffset grantedAtUtc,
        decimal discountPercent,
        GrantStatus status,
        string idempotencyKey)
    {
        Id = id;
        CampaignId = campaignId;
        AgentId = agentId;
        CustomerExternalId = customerExternalId;
        CustomerNameAtGrant = customerNameAtGrant;
        BusinessDate = businessDate;
        GrantedAtUtc = grantedAtUtc;
        DiscountPercent = discountPercent;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }

    public Guid Id { get; private set; }

    public Guid CampaignId { get; private set; }

    public Guid AgentId { get; private set; }

    public string CustomerExternalId { get; private set; }

    /// <summary>P-07: the catalogue name as it was at the moment of the grant, never refreshed.</summary>
    public string CustomerNameAtGrant { get; private set; }

    public DateOnly BusinessDate { get; private set; }

    public DateTimeOffset GrantedAtUtc { get; private set; }

    /// <summary>P-07: copied from the campaign at the moment of the grant, for the same reason.</summary>
    public decimal DiscountPercent { get; private set; }

    public GrantStatus Status { get; private set; }

    public string IdempotencyKey { get; private set; }

    public DateTimeOffset? VoidedAtUtc { get; private set; }

    /// <summary>
    /// The subject claim of whoever voided the grant, not a foreign key to Agent: an admin may void
    /// somebody else's grant and an admin is not a row in the Agent table.
    /// </summary>
    public string? VoidedByExternalUserId { get; private set; }

    public string? VoidReason { get; private set; }

    public bool IsActive => Status == GrantStatus.Active;

    public static RewardGrant Create(
        Guid id,
        Guid campaignId,
        Guid agentId,
        string customerExternalId,
        string customerNameAtGrant,
        DateOnly businessDate,
        DateTimeOffset grantedAtUtc,
        decimal discountPercent,
        string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(customerExternalId))
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "Customer external id is required.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "Idempotency key is required.");
        }

        return new RewardGrant(
            id,
            campaignId,
            agentId,
            customerExternalId,
            customerNameAtGrant,
            businessDate,
            grantedAtUtc,
            discountPercent,
            GrantStatus.Active,
            idempotencyKey);
    }

    /// <summary>
    /// P-04 and P-05: voiding records the reason, the actor and the time, and nothing else changes.
    /// The slot the grant occupied is freed automatically, because the daily count only counts
    /// active grants. In the database this transition is executed as a conditional update, so this
    /// method is the same rule expressed in memory rather than a second, competing rule.
    /// </summary>
    public void Void(string voidedByExternalUserId, string? reason, DateTimeOffset voidedAtUtc)
    {
        if (Status != GrantStatus.Active)
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.GrantAlreadyVoided,
                "The grant has already been voided.");
        }

        if (string.IsNullOrWhiteSpace(voidedByExternalUserId))
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "The external user id of the actor is required when voiding a grant.");
        }

        if (reason is { Length: > MaxVoidReasonLength })
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                $"Void reason cannot be longer than {MaxVoidReasonLength} characters.");
        }

        Status = GrantStatus.Voided;
        VoidedByExternalUserId = voidedByExternalUserId;
        VoidReason = reason;
        VoidedAtUtc = voidedAtUtc;
    }
}
