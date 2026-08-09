namespace Campaign.Core.Domain;

/// <summary>
/// The only exception the domain throws. <see cref="Code"/> is the machine readable error type from
/// the API error catalogue, so the web layer maps it to a ProblemDetails response without a chain of
/// type checks. <see cref="Details"/> carries the extra values a specific error has to report, such
/// as used and limit for daily-limit-reached.
/// </summary>
public sealed class DomainRuleViolationException : Exception
{
    private static readonly IReadOnlyDictionary<string, object?> NoDetails =
        new Dictionary<string, object?>();

    public DomainRuleViolationException(string code, string message)
        : this(code, message, NoDetails)
    {
    }

    public DomainRuleViolationException(string code, string message, IReadOnlyDictionary<string, object?> details)
        : base(message)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; }

    public IReadOnlyDictionary<string, object?> Details { get; }
}
