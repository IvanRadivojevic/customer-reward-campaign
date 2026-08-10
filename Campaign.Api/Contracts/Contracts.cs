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

/// <summary>A customer as the external catalogue knows them. Example: id 1, name "Ana Anic".</summary>
public sealed record CustomerResponse(string ExternalId, string Name)
{
    public static CustomerResponse From(CustomerDto customer) => new(customer.ExternalId, customer.Name);
}
