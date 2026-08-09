namespace Campaign.Tests.Fakes;

using Campaign.Core.Ports;

/// <summary>
/// Stands in for the SOAP catalogue. The tests never go to the network; that the port has a second
/// implementation this cheap is the point of the port existing at all.
/// </summary>
public sealed class FakeCustomerDirectory : ICustomerDirectory
{
    private readonly Dictionary<string, CustomerDto> _customers = [];

    public FakeCustomerDirectory With(string externalId, string name)
    {
        _customers[externalId] = new CustomerDto(externalId, name);
        return this;
    }

    /// <summary>Lets a test change the catalogue after a grant was made, to prove P-07.</summary>
    public void Rename(string externalId, string newName)
    {
        _customers[externalId] = new CustomerDto(externalId, newName);
    }

    public Task<CustomerDto?> FindByIdAsync(string externalCustomerId, CancellationToken ct) =>
        Task.FromResult(_customers.GetValueOrDefault(externalCustomerId));

    public Task<IReadOnlyList<CustomerDto>> SearchByNameAsync(string name, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CustomerDto>>(
            _customers.Values
                .Where(customer => customer.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                .ToList());
}
