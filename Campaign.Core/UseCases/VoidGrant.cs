namespace Campaign.Core.UseCases;

using Campaign.Core.Domain;
using Campaign.Core.Ports;

/// <summary>
/// The actor is identified by the subject claim of the token; an admin may void any grant, an agent
/// only their own.
/// </summary>
public sealed record VoidGrantCommand(
    Guid GrantId,
    string ActorExternalUserId,
    bool ActorIsAdmin,
    string? Reason);

/// <summary>
/// Voids a grant. Nothing is deleted and no business field is edited (P-05); the correction of a
/// mistake is this void followed by a new grant.
/// </summary>
public sealed class VoidGrant
{
    private readonly IGrantRepository _grants;
    private readonly BusinessDateProvider _businessDates;

    public VoidGrant(IGrantRepository grants, BusinessDateProvider businessDates)
    {
        _grants = grants;
        _businessDates = businessDates;
    }

    public async Task ExecuteAsync(VoidGrantCommand command, CancellationToken ct)
    {
        if (command.Reason is { Length: > RewardGrant.MaxVoidReasonLength })
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                $"Void reason cannot be longer than {RewardGrant.MaxVoidReasonLength} characters.");
        }

        var grant = await _grants.FindByIdAsync(command.GrantId, ct)
            ?? throw new DomainRuleViolationException(DomainErrorCodes.GrantNotFound, "Unknown grant.");

        if (!command.ActorIsAdmin)
        {
            var agent = await _grants.FindAgentByExternalUserIdAsync(command.ActorExternalUserId, ct)
                ?? throw new DomainRuleViolationException(
                    DomainErrorCodes.AgentNotActive,
                    "The token does not belong to a known agent.");

            // A deactivated agent may still read, but may no longer change grants.
            if (!agent.IsActive)
            {
                throw new DomainRuleViolationException(
                    DomainErrorCodes.AgentNotActive,
                    "The agent is not active and cannot void grants.");
            }

            if (grant.AgentId != agent.Id)
            {
                throw new DomainRuleViolationException(
                    DomainErrorCodes.ForbiddenAgentScope,
                    "An agent can only void their own grants.");
            }
        }

        // P-04 and P-05: the conditional void reports whether it actually changed a row. Zero rows
        // means somebody voided the grant first, and that is the only way this can fail.
        var voided = await _grants.TryVoidAsync(
            grant.Id,
            command.ActorExternalUserId,
            command.Reason,
            _businessDates.UtcNow(),
            ct);

        if (!voided)
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.GrantAlreadyVoided,
                "The grant has already been voided.");
        }
    }
}
