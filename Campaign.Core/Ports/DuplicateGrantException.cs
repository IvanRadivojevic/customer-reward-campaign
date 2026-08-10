namespace Campaign.Core.Ports;

/// <summary>Which uniqueness rule the store refused when two requests raced.</summary>
public enum DuplicateGrantReason
{
    /// <summary>P-03: the customer already has an active grant in that campaign.</summary>
    CustomerAlreadyRewarded,

    /// <summary>P-06: the agent already used that idempotency key.</summary>
    IdempotencyKeyAlreadyUsed
}

/// <summary>
/// The store refused the insert because another request got there first. The rules are checked in
/// the use case, but under load two requests can both pass the check before either writes, and the
/// unique index is what actually decides. This exception carries the store's verdict back to the use
/// case in domain terms, so nothing outside the infrastructure has to know about index names.
/// </summary>
public sealed class DuplicateGrantException : Exception
{
    public DuplicateGrantException(DuplicateGrantReason reason, Exception innerException)
        : base($"The grant was refused by the database because of {reason}.", innerException)
    {
        Reason = reason;
    }

    public DuplicateGrantReason Reason { get; }
}
