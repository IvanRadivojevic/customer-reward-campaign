namespace Campaign.Api.Contracts;

using Campaign.Core.Domain;
using Campaign.Core.Ports;
using Campaign.Core.UseCases;

/// <summary>
/// What a caller sends to award a discount. The campaign comes from the route and the agent from the
/// token, so the body carries only the customer.
/// </summary>
/// <param name="CustomerExternalId">Customer id in the external catalogue. Example: 1</param>
public sealed record CreateGrantRequest(string CustomerExternalId);

/// <summary>Why a grant is being voided. Example: "wrong customer picked from the list".</summary>
/// <param name="Reason">Free text, at most 500 characters.</param>
public sealed record VoidGrantRequest(string? Reason);

/// <summary>A grant as the API publishes it - deliberately not the entity.</summary>
public sealed record GrantResponse(
    Guid Id,
    Guid CampaignId,
    Guid AgentId,
    string CustomerExternalId,
    string CustomerNameAtGrant,
    DateOnly BusinessDate,
    DateTimeOffset GrantedAtUtc,
    decimal DiscountPercent,
    string Status,
    DateTimeOffset? VoidedAtUtc,
    string? VoidedByExternalUserId,
    string? VoidReason)
{
    public static GrantResponse From(RewardGrant grant) => new(
        grant.Id,
        grant.CampaignId,
        grant.AgentId,
        grant.CustomerExternalId,
        grant.CustomerNameAtGrant,
        grant.BusinessDate,
        grant.GrantedAtUtc,
        grant.DiscountPercent,
        grant.Status.ToString(),
        grant.VoidedAtUtc,
        grant.VoidedByExternalUserId,
        grant.VoidReason);
}

/// <summary>How much of today's limit an agent has used. Example: 2 of 5.</summary>
public sealed record QuotaResponse(
    Guid CampaignId,
    Guid AgentId,
    DateOnly BusinessDate,
    int Used,
    int Limit)
{
    public static QuotaResponse From(QuotaView quota) =>
        new(quota.CampaignId, quota.AgentId, quota.BusinessDate, quota.Used, quota.Limit);
}

/// <summary>
/// The summary of one processed purchase report. <see cref="AlreadyImported"/> is true when this
/// exact file had already been imported into this campaign and no second batch was made.
/// </summary>
public sealed record ImportBatchResponse(
    Guid Id,
    Guid CampaignId,
    string FileName,
    string FileSha256,
    DateTimeOffset UploadedAtUtc,
    string UploadedBy,
    int RowsTotal,
    int RowsMatched,
    int RowsUnmatched,
    int RowsInvalid,
    string Status,
    bool AlreadyImported)
{
    public static ImportBatchResponse From(ImportBatch batch, bool alreadyImported) => new(
        batch.Id,
        batch.CampaignId,
        batch.FileName,
        batch.FileSha256,
        batch.UploadedAtUtc,
        batch.UploadedBy,
        batch.RowsTotal,
        batch.RowsMatched,
        batch.RowsUnmatched,
        batch.RowsInvalid,
        batch.Status.ToString(),
        alreadyImported);
}

/// <summary>One processed row, exactly as it was stored - including the ones that could not be read.</summary>
public sealed record PurchaseRowResponse(
    int RowNumber,
    string MatchStatus,
    string? CustomerExternalId,
    DateOnly? PurchaseDate,
    decimal? Amount,
    string? Currency,
    Guid? MatchedGrantId,
    string? Error,
    string RawLine)
{
    public static PurchaseRowResponse From(PurchaseResult row) => new(
        row.RowNumber,
        row.MatchStatus.ToString(),
        row.CustomerExternalId,
        row.PurchaseDate,
        row.Amount,
        row.Currency,
        row.MatchedGrantId,
        row.Error,
        row.RawLine);
}

/// <summary>An import with the rows it produced.</summary>
public sealed record ImportBatchDetailResponse(ImportBatchResponse Batch, IReadOnlyList<PurchaseRowResponse> Rows)
{
    public static ImportBatchDetailResponse From(ImportBatchView view) => new(
        ImportBatchResponse.From(view.Batch, alreadyImported: false),
        view.Rows.Select(PurchaseRowResponse.From).ToList());
}

/// <summary>
/// The campaign report. Every number is read from the vw_CampaignResults view; conversionRate is
/// convertedGrants over activeGrants, so it cannot pass 100%.
/// </summary>
public sealed record CampaignResultsResponse(
    Guid CampaignId,
    ResultTotalsResponse Totals,
    IReadOnlyList<ResultRowResponse> Rows)
{
    public static CampaignResultsResponse From(CampaignResultsView view) => new(
        view.CampaignId,
        new ResultTotalsResponse(
            view.Totals.ActiveGrants,
            view.Totals.VoidedGrants,
            view.Totals.ConvertedGrants,
            view.Totals.MatchedRows,
            view.Totals.ConversionRate),
        view.Rows.Select(ResultRowResponse.From).ToList());
}

public sealed record ResultTotalsResponse(
    int ActiveGrants,
    int VoidedGrants,
    int ConvertedGrants,
    int MatchedRows,
    decimal ConversionRate);

/// <summary>One group: an agent, or a business date, depending on groupBy.</summary>
public sealed record ResultRowResponse(
    string Key,
    string DisplayName,
    int ActiveGrants,
    int VoidedGrants,
    int ConvertedGrants,
    int MatchedRows,
    decimal ConversionRate)
{
    public static ResultRowResponse From(ResultGroup group) => new(
        group.Key,
        group.DisplayName,
        group.ActiveGrants,
        group.VoidedGrants,
        group.ConvertedGrants,
        group.MatchedRows,
        group.ConversionRate);
}

/// <summary>A customer as the external catalogue knows them. Example: id 1, name "Ana Anic".</summary>
public sealed record CustomerResponse(string ExternalId, string Name)
{
    public static CustomerResponse From(CustomerDto customer) => new(customer.ExternalId, customer.Name);
}
