namespace Campaign.Core.UseCases;

using Campaign.Core.Domain;
using Campaign.Core.Ports;

/// <summary>How the rows are grouped. Both shapes are the same; only the key differs.</summary>
public enum ResultsGrouping
{
    Agent,
    Day
}

public sealed record ResultTotals(
    int ActiveGrants,
    int VoidedGrants,
    int ConvertedGrants,
    int MatchedRows,
    decimal ConversionRate);

public sealed record ResultGroup(
    string Key,
    string DisplayName,
    int ActiveGrants,
    int VoidedGrants,
    int ConvertedGrants,
    int MatchedRows,
    decimal ConversionRate);

public sealed record CampaignResultsView(Guid CampaignId, ResultTotals Totals, IReadOnlyList<ResultGroup> Rows);

/// <summary>
/// The campaign report. Every number comes out of the results view, so the counting is not written a
/// second time here; what this adds is the grouping the caller asked for and the one division P-09
/// defines.
/// </summary>
public sealed class GetCampaignResults
{
    private readonly IReportRepository _reports;
    private readonly IGrantRepository _grants;

    public GetCampaignResults(IReportRepository reports, IGrantRepository grants)
    {
        _reports = reports;
        _grants = grants;
    }

    public async Task<CampaignResultsView> ExecuteAsync(
        Guid campaignId,
        ResultsGrouping grouping,
        CancellationToken ct)
    {
        var campaign = await _grants.FindCampaignAsync(campaignId, ct)
            ?? throw new DomainRuleViolationException(
                DomainErrorCodes.CampaignNotActive,
                "The campaign does not exist.");

        var rows = await _reports.ReadResultsAsync(campaign.Id, ct);

        var groups = grouping switch
        {
            ResultsGrouping.Agent => rows
                .GroupBy(row => (Key: row.AgentExternalUserId, Name: row.AgentDisplayName))
                .Select(group => Summarise(group.Key.Key, group.Key.Name, group))
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList(),

            ResultsGrouping.Day => rows
                .GroupBy(row => row.BusinessDate)
                .Select(group => Summarise(Format(group.Key), Format(group.Key), group))
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList(),

            _ => throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "groupBy has to be either 'agent' or 'day'.")
        };

        return new CampaignResultsView(campaign.Id, TotalsOf(rows), groups);
    }

    private static ResultGroup Summarise(string key, string displayName, IEnumerable<CampaignResultRow> rows)
    {
        var counted = rows.ToList();
        var activeGrants = counted.Sum(row => row.ActiveGrants);
        var convertedGrants = counted.Sum(row => row.ConvertedGrants);

        return new ResultGroup(
            key,
            displayName,
            activeGrants,
            counted.Sum(row => row.VoidedGrants),
            convertedGrants,
            counted.Sum(row => row.MatchedRows),
            ConversionRate(convertedGrants, activeGrants));
    }

    private static ResultTotals TotalsOf(IReadOnlyList<CampaignResultRow> rows)
    {
        var activeGrants = rows.Sum(row => row.ActiveGrants);
        var convertedGrants = rows.Sum(row => row.ConvertedGrants);

        return new ResultTotals(
            activeGrants,
            rows.Sum(row => row.VoidedGrants),
            convertedGrants,
            rows.Sum(row => row.MatchedRows),
            ConversionRate(convertedGrants, activeGrants));
    }

    /// <summary>
    /// P-09. Converted grants over active grants, so it cannot pass 100%: a customer who bought three
    /// times still converted one grant, and those three purchases are reported as matchedRows.
    /// </summary>
    private static decimal ConversionRate(int convertedGrants, int activeGrants) =>
        activeGrants == 0 ? 0m : Math.Round((decimal)convertedGrants / activeGrants, 4);

    private static string Format(DateOnly businessDate) => businessDate.ToString("yyyy-MM-dd");
}
