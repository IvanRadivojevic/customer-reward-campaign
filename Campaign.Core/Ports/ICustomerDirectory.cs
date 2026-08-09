namespace Campaign.Core.Ports;

/// <summary>A customer as the external catalogue knows them.</summary>
public sealed record CustomerDto(string ExternalId, string Name);

/// <summary>
/// The external customer catalogue. The SOAP service behind it is one implementation; an in-memory
/// one keeps the demo working offline, and a Dataverse one would connect the same use cases to
/// Dynamics 365 without touching this project.
/// </summary>
public interface ICustomerDirectory
{
    Task<CustomerDto?> FindByIdAsync(string externalCustomerId, CancellationToken ct);

    Task<IReadOnlyList<CustomerDto>> SearchByNameAsync(string name, CancellationToken ct);
}
