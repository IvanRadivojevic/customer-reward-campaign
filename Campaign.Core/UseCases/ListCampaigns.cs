namespace Campaign.Core.UseCases;

using Campaign.Core.Ports;

/// <summary>
/// The campaigns an agent can work in. It exists so the form can offer a list instead of asking
/// somebody to paste a campaign id it has no way of knowing.
/// </summary>
public sealed class ListCampaigns
{
    private readonly IGrantRepository _grants;

    public ListCampaigns(IGrantRepository grants)
    {
        _grants = grants;
    }

    public async Task<IReadOnlyList<Domain.Campaign>> ExecuteAsync(CancellationToken ct) =>
        await _grants.ListCampaignsAsync(ct);
}
