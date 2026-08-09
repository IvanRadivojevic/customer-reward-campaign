namespace Campaign.Core.Domain;

/// <summary>
/// One row of a purchase report, kept exactly as it was processed - including the rows that could
/// not be read. All parsed fields are nullable for that reason, and the three factory methods are
/// the only way to build a row, so an impossible combination cannot be created at all.
/// </summary>
public sealed class PurchaseResult
{
    public const int CurrencyCodeLength = 3;

    private PurchaseResult(
        Guid id,
        Guid batchId,
        int rowNumber,
        string rawLine,
        string? customerExternalId,
        DateOnly? purchaseDate,
        decimal? amount,
        string? currency,
        Guid? matchedGrantId,
        MatchStatus matchStatus,
        string? error)
    {
        Id = id;
        BatchId = batchId;
        RowNumber = rowNumber;
        RawLine = rawLine;
        CustomerExternalId = customerExternalId;
        PurchaseDate = purchaseDate;
        Amount = amount;
        Currency = currency;
        MatchedGrantId = matchedGrantId;
        MatchStatus = matchStatus;
        Error = error;
    }

    public Guid Id { get; private set; }

    public Guid BatchId { get; private set; }

    public int RowNumber { get; private set; }

    public string RawLine { get; private set; }

    public string? CustomerExternalId { get; private set; }

    public DateOnly? PurchaseDate { get; private set; }

    public decimal? Amount { get; private set; }

    public string? Currency { get; private set; }

    public Guid? MatchedGrantId { get; private set; }

    public MatchStatus MatchStatus { get; private set; }

    public string? Error { get; private set; }

    /// <summary>The customer had an active grant in this campaign, so the row counts as a conversion.</summary>
    public static PurchaseResult Matched(
        Guid id,
        Guid batchId,
        int rowNumber,
        string rawLine,
        string customerExternalId,
        DateOnly purchaseDate,
        decimal? amount,
        string? currency,
        Guid matchedGrantId)
    {
        GuardParsedRow(customerExternalId, amount, currency);

        return new PurchaseResult(
            id,
            batchId,
            rowNumber,
            rawLine,
            customerExternalId,
            purchaseDate,
            amount,
            currency,
            matchedGrantId,
            MatchStatus.Matched,
            error: null);
    }

    /// <summary>
    /// The row was read correctly but there is no active grant for that customer in this campaign,
    /// including the case where the grant was voided. It is reported, never dropped silently.
    /// </summary>
    public static PurchaseResult Unmatched(
        Guid id,
        Guid batchId,
        int rowNumber,
        string rawLine,
        string customerExternalId,
        DateOnly purchaseDate,
        decimal? amount,
        string? currency)
    {
        GuardParsedRow(customerExternalId, amount, currency);

        return new PurchaseResult(
            id,
            batchId,
            rowNumber,
            rawLine,
            customerExternalId,
            purchaseDate,
            amount,
            currency,
            matchedGrantId: null,
            MatchStatus.Unmatched,
            error: null);
    }

    /// <summary>
    /// The row could not be read. No parsed field is kept: a value taken out of a broken row is not
    /// data anybody should trust. The raw line and the error carry the whole story.
    /// </summary>
    public static PurchaseResult Invalid(Guid id, Guid batchId, int rowNumber, string rawLine, string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "An invalid row has to say why it is invalid.");
        }

        return new PurchaseResult(
            id,
            batchId,
            rowNumber,
            rawLine,
            customerExternalId: null,
            purchaseDate: null,
            amount: null,
            currency: null,
            matchedGrantId: null,
            MatchStatus.Invalid,
            error);
    }

    private static void GuardParsedRow(string customerExternalId, decimal? amount, string? currency)
    {
        if (string.IsNullOrWhiteSpace(customerExternalId))
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "A row that is not invalid has to carry a customer external id.");
        }

        // Amount and currency are optional by design, but they travel as a pair: an amount without
        // a currency is ambiguous data.
        if (amount.HasValue != (currency is not null))
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "Amount and currency have to be either both present or both absent.");
        }

        if (currency is not null && currency.Length != CurrencyCodeLength)
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                $"Currency has to be a {CurrencyCodeLength} letter code.");
        }
    }
}
