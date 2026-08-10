namespace Campaign.Core.Ports;

using Campaign.Core.Domain;

/// <summary>
/// One row of the campaign results view: the counts for one agent on one business date. The grain is
/// deliberate - a grant belongs to exactly one agent and exactly one date, so grouping by agent or by
/// day is a matter of adding these rows up, and no grant is ever counted twice.
/// </summary>
public sealed record CampaignResultRow
{
    public Guid CampaignId { get; init; }

    public Guid AgentId { get; init; }

    public string AgentExternalUserId { get; init; } = string.Empty;

    public string AgentDisplayName { get; init; } = string.Empty;

    public DateOnly BusinessDate { get; init; }

    public int ActiveGrants { get; init; }

    public int VoidedGrants { get; init; }

    /// <summary>P-09 numerator: active grants that at least one purchase row points at.</summary>
    public int ConvertedGrants { get; init; }

    /// <summary>
    /// Reported next to it, never instead of it: one grant can carry several purchases. This counts
    /// the rows that were matched when the file was imported, whatever happened to the grant since -
    /// voiding a grant does not un-match a purchase that already took place.
    /// </summary>
    public int MatchedRows { get; init; }
}

/// <summary>
/// Reads what was written, never the live catalogue. The counting itself lives in the T-SQL view, so
/// the same arithmetic is not written a second time here and SSRS can read the same numbers.
/// </summary>
public interface IReportRepository
{
    Task<IReadOnlyList<CampaignResultRow>> ReadResultsAsync(Guid campaignId, CancellationToken ct);

    Task<IReadOnlyList<PurchaseResult>> ListUnmatchedAsync(Guid campaignId, CancellationToken ct);
}
