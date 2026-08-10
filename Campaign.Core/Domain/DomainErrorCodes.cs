namespace Campaign.Core.Domain;

/// <summary>
/// The error types from the API error catalogue that the domain itself can raise. They live in one
/// place so the use cases and the ProblemDetails mapping in the web layer cannot drift apart.
/// </summary>
public static class DomainErrorCodes
{
    public const string ValidationFailed = "validation-failed";
    public const string ForbiddenAgentScope = "forbidden-agent-scope";

    /// <summary>
    /// The token is valid but its subject is not an agent who may own records: either no agent
    /// carries that subject, or the agent is deactivated. A genuine 401 is the authentication
    /// middleware's answer, never a use case's, which is why the domain does not raise one.
    /// </summary>
    public const string AgentNotActive = "agent-not-active";

    public const string CustomerNotFound = "customer-not-found";
    public const string GrantNotFound = "grant-not-found";
    public const string ImportBatchNotFound = "import-batch-not-found";

    /// <summary>
    /// The file cannot be read as a purchase report at all - empty, not a CSV, or missing a required
    /// column. A single unreadable row is not this: that row becomes an Invalid result and the rest
    /// of the file is still processed.
    /// </summary>
    public const string CsvInvalid = "csv-invalid";
    public const string CampaignNotActive = "campaign-not-active";
    public const string DailyLimitReached = "daily-limit-reached";
    public const string CustomerAlreadyRewarded = "customer-already-rewarded";
    public const string GrantAlreadyVoided = "grant-already-voided";
    public const string IdempotencyKeyReused = "idempotency-key-reused";
}
