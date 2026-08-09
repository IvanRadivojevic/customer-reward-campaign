namespace Campaign.Core.Domain;

/// <summary>
/// A call centre agent. <see cref="ExternalUserId"/> is the subject claim of the JWT, which is how a
/// request is tied to the agent who owns the records it creates.
/// </summary>
public sealed class Agent
{
    private Agent(Guid id, string externalUserId, string displayName, bool isActive)
    {
        Id = id;
        ExternalUserId = externalUserId;
        DisplayName = displayName;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }

    public string ExternalUserId { get; private set; }

    public string DisplayName { get; private set; }

    public bool IsActive { get; private set; }

    public static Agent Create(Guid id, string externalUserId, string displayName, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(externalUserId))
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "Agent external user id is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "Agent display name is required.");
        }

        return new Agent(id, externalUserId, displayName, isActive);
    }
}
