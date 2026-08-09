namespace Campaign.Core.UseCases;

using Campaign.Core.Domain;
using Campaign.Core.Ports;

public sealed record CreateGrantCommand(
    Guid CampaignId,
    string CustomerExternalId,
    string AgentExternalUserId,
    string IdempotencyKey);

/// <summary><see cref="Replayed"/> is true when the key had already been used for the same request.</summary>
public sealed record CreateGrantResult(RewardGrant Grant, bool Replayed);

/// <summary>
/// Awards a discount to a customer. The order of the checks is deliberate: a replay is answered
/// before anything else, the catalogue is asked before the transaction is opened so no network call
/// happens inside it, and only the count and the insert run under the transaction.
/// </summary>
public sealed class CreateGrant
{
    private readonly IGrantRepository _grants;
    private readonly ICustomerDirectory _customers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly BusinessDateProvider _businessDates;

    public CreateGrant(
        IGrantRepository grants,
        ICustomerDirectory customers,
        IUnitOfWork unitOfWork,
        BusinessDateProvider businessDates)
    {
        _grants = grants;
        _customers = customers;
        _unitOfWork = unitOfWork;
        _businessDates = businessDates;
    }

    public async Task<CreateGrantResult> ExecuteAsync(CreateGrantCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.CustomerExternalId))
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "Customer external id is required.");
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "The Idempotency-Key header is required.");
        }

        var agent = await _grants.FindAgentByExternalUserIdAsync(command.AgentExternalUserId, ct)
            ?? throw new DomainRuleViolationException(
                DomainErrorCodes.AgentNotActive,
                "The token does not belong to a known agent.");

        // P-06. A replay is answered even if the campaign has closed, the agent has since run out of
        // quota or the agent has been deactivated: the caller is asking about a grant that was
        // already made, and answering it creates nothing.
        var existing = await _grants.FindByIdempotencyKeyAsync(agent.Id, command.IdempotencyKey, ct);
        if (existing is not null)
        {
            var sameRequest = existing.CampaignId == command.CampaignId
                && string.Equals(existing.CustomerExternalId, command.CustomerExternalId, StringComparison.Ordinal);

            if (!sameRequest)
            {
                throw new DomainRuleViolationException(
                    DomainErrorCodes.IdempotencyKeyReused,
                    "This idempotency key has already been used for a different grant.");
            }

            return new CreateGrantResult(existing, Replayed: true);
        }

        // A deactivated agent may still read, but may no longer award anything new.
        if (!agent.IsActive)
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.AgentNotActive,
                "The agent is not active and cannot create grants.");
        }

        // An unknown campaign is certainly not an active one, so it shares the P-01 answer.
        var campaign = await _grants.FindCampaignAsync(command.CampaignId, ct)
            ?? throw new DomainRuleViolationException(
                DomainErrorCodes.CampaignNotActive,
                "The campaign does not exist.");

        var businessDate = _businessDates.Today();

        // P-01.
        if (!campaign.IsOpenOn(businessDate))
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.CampaignNotActive,
                "The campaign is not active on this business date.");
        }

        // The catalogue is the source of the customer name frozen on the grant (P-07). If it is
        // down the adapter throws and no grant is created - there is no "pending check" state.
        var customer = await _customers.FindByIdAsync(command.CustomerExternalId, ct)
            ?? throw new DomainRuleViolationException(
                DomainErrorCodes.CustomerNotFound,
                "The customer catalogue does not know this customer.");

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // P-03.
                var alreadyRewarded = await _grants.FindActiveGrantForCustomerAsync(
                    campaign.Id,
                    command.CustomerExternalId,
                    token);

                if (alreadyRewarded is not null)
                {
                    throw new DomainRuleViolationException(
                        DomainErrorCodes.CustomerAlreadyRewarded,
                        "This customer already has an active grant in this campaign.",
                        new Dictionary<string, object?> { ["grantId"] = alreadyRewarded.Id });
                }

                // P-02. Only active grants are counted, which is why voiding frees a slot by itself.
                var used = await _grants.CountActiveGrantsAsync(agent.Id, campaign.Id, businessDate, token);
                if (used >= campaign.DailyLimitPerAgent)
                {
                    throw new DomainRuleViolationException(
                        DomainErrorCodes.DailyLimitReached,
                        "The agent has reached the daily limit for this campaign.",
                        new Dictionary<string, object?>
                        {
                            ["used"] = used,
                            ["limit"] = campaign.DailyLimitPerAgent
                        });
                }

                var grant = RewardGrant.Create(
                    Guid.NewGuid(),
                    campaign.Id,
                    agent.Id,
                    command.CustomerExternalId,
                    customer.Name,
                    businessDate,
                    _businessDates.UtcNow(),
                    campaign.DiscountPercent,
                    command.IdempotencyKey);

                await _grants.AddAsync(grant, token);
                await _unitOfWork.SaveChangesAsync(token);

                return new CreateGrantResult(grant, Replayed: false);
            },
            ct);
    }
}
