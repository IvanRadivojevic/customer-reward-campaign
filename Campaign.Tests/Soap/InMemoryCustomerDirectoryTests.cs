namespace Campaign.Tests.Soap;

using Campaign.Infrastructure.Soap;

/// <summary>
/// The second implementation of the port. If these pass and the SOAP ones pass, the port is doing
/// the job it exists for.
/// </summary>
public class InMemoryCustomerDirectoryTests
{
    private readonly InMemoryCustomerDirectory _directory = new();

    [Fact]
    public async Task A_known_customer_is_found_by_id()
    {
        var customer = await _directory.FindByIdAsync("1", CancellationToken.None);

        Assert.NotNull(customer);
        Assert.Equal("1", customer.ExternalId);
    }

    [Fact]
    public async Task An_unknown_id_gives_nothing_back()
    {
        Assert.Null(await _directory.FindByIdAsync("does-not-exist", CancellationToken.None));
    }

    [Fact]
    public async Task A_search_ignores_letter_case()
    {
        var matches = await _directory.SearchByNameAsync("ANA", CancellationToken.None);

        Assert.Contains(matches, customer => customer.Name == "Ana Anic");
    }

    [Fact]
    public async Task An_empty_search_returns_nothing_instead_of_everything()
    {
        Assert.Empty(await _directory.SearchByNameAsync("   ", CancellationToken.None));
    }
}
