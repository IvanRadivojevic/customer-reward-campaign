namespace Campaign.Infrastructure.Soap;

using Campaign.Core.Ports;

/// <summary>
/// The second implementation of the same port. It exists for two reasons: it proves the port is not
/// decoration, and it keeps the demo working when the public SOAP service cannot be reached.
/// Selected with Directory:Provider = InMemory.
/// </summary>
public sealed class InMemoryCustomerDirectory : ICustomerDirectory
{
    private static readonly IReadOnlyList<CustomerDto> Customers =
    [
        new("1", "Ana Anic"),
        new("2", "Bojan Bojic"),
        new("3", "Vesna Vesic"),
        new("4", "Goran Goric"),
        new("5", "Dragana Dragic"),
        new("6", "Marko Markovic"),
        new("7", "Jelena Jelic"),
        new("8", "Nikola Nikolic")
    ];

    public Task<CustomerDto?> FindByIdAsync(string externalCustomerId, CancellationToken ct) =>
        Task.FromResult(Customers.FirstOrDefault(customer =>
            string.Equals(customer.ExternalId, externalCustomerId, StringComparison.Ordinal)));

    public Task<IReadOnlyList<CustomerDto>> SearchByNameAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult<IReadOnlyList<CustomerDto>>([]);
        }

        return Task.FromResult<IReadOnlyList<CustomerDto>>(
            Customers
                .Where(customer => customer.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                .ToList());
    }
}
