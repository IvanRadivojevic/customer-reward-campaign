namespace Campaign.Tests.Soap;

using System.ServiceModel;
using Campaign.Core.Ports;
using Campaign.Infrastructure.Soap;
using Campaign.Infrastructure.Soap.Generated;

public class SoapCustomerDirectoryTests
{
    private const string FindPersonFixture = "synthetic-FindPerson-response.xml";
    private const string GetListByNameFixture = "synthetic-GetListByName-response.xml";

    [Fact]
    public async Task The_person_from_the_catalogue_becomes_a_customer_carrying_the_id_that_was_asked_for()
    {
        // Person has no id of its own in the contract, so this is the only id the adapter can use.
        var person = SoapFixtures.LoadPerson(FindPersonFixture);
        var factory = FakeSoapClientFactory.Always(() => FakeSoapDemoSoap.Returning(person));
        var directory = new SoapCustomerDirectory(factory.Create);

        var customer = await directory.FindByIdAsync("1", CancellationToken.None);

        Assert.NotNull(customer);
        Assert.Equal("1", customer.ExternalId);
        Assert.Equal("Ana Anic", customer.Name);
    }

    [Fact]
    public async Task An_unknown_customer_comes_back_as_nothing_rather_than_as_a_failure()
    {
        var empty = new Person { Name = string.Empty, SSN = string.Empty };
        var directory = Directory(FakeSoapClientFactory.Always(() => FakeSoapDemoSoap.Returning(empty)));

        Assert.Null(await directory.FindByIdAsync("does-not-exist", CancellationToken.None));
    }

    [Fact]
    public async Task A_search_maps_every_row_that_carries_an_id()
    {
        var rows = SoapFixtures.LoadPersonIdentifications(GetListByNameFixture);
        var directory = Directory(FakeSoapClientFactory.Always(() => FakeSoapDemoSoap.Returning(rows)));

        var matches = await directory.SearchByNameAsync("a", CancellationToken.None);

        // The fixture holds three rows and one of them has an empty id, which is not a customer
        // anything can be granted to.
        Assert.Equal(2, matches.Count);
        Assert.Equal(["1", "7"], matches.Select(match => match.ExternalId));
        Assert.Equal(["Ana Anic", "Jelena Jelic"], matches.Select(match => match.Name));
    }

    [Fact]
    public async Task Every_attempt_uses_its_own_channel_and_the_broken_one_is_aborted_rather_than_closed()
    {
        // A WCF channel does not survive its first failure. If all three attempts shared one client,
        // the retries would be spent on a faulted channel instead of on the service.
        var person = SoapFixtures.LoadPerson(FindPersonFixture);
        var factory = FakeSoapClientFactory.PerAttempt(attempt => attempt == 0
            ? FakeSoapDemoSoap.Throwing(new CommunicationException("the connection dropped"))
            : FakeSoapDemoSoap.Returning(person));

        var customer = await Directory(factory).FindByIdAsync("1", CancellationToken.None);

        Assert.Equal("Ana Anic", customer?.Name);
        Assert.Equal(2, factory.Attempts);
        Assert.NotSame(factory.Created[0], factory.Created[1]);

        // The failed attempt is aborted, because closing a faulted channel throws a second exception
        // and would bury the real reason. The successful one is closed properly.
        Assert.True(factory.Created[0].WasAborted);
        Assert.False(factory.Created[0].WasClosed);
        Assert.True(factory.Created[1].WasClosed);
        Assert.False(factory.Created[1].WasAborted);

        // Each channel was used exactly once.
        Assert.Equal([1, 1], factory.Created.Select(client => client.Calls));
    }

    [Fact]
    public async Task A_dropped_connection_is_tried_two_more_times_and_then_reported_as_unavailable()
    {
        var factory = FakeSoapClientFactory.Always(
            () => FakeSoapDemoSoap.Throwing(new CommunicationException("the connection dropped")));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(
            () => Directory(factory).FindByIdAsync("1", CancellationToken.None));

        Assert.Equal(3, factory.Attempts);
        Assert.Equal(3, factory.TotalCalls);
        Assert.All(factory.Created, client => Assert.True(client.WasAborted));
    }

    [Fact]
    public async Task A_timeout_is_retried_the_same_way_a_dropped_connection_is()
    {
        var factory = FakeSoapClientFactory.Always(
            () => FakeSoapDemoSoap.Throwing(new TimeoutException("no answer in time")));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(
            () => Directory(factory).FindByIdAsync("1", CancellationToken.None));

        Assert.Equal(3, factory.Attempts);
    }

    [Fact]
    public async Task A_soap_fault_is_not_retried_because_the_service_did_answer()
    {
        var factory = FakeSoapClientFactory.Always(
            () => FakeSoapDemoSoap.Throwing(new FaultException("the request was rejected")));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(
            () => Directory(factory).FindByIdAsync("1", CancellationToken.None));

        Assert.Equal(1, factory.Attempts);
    }

    [Fact]
    public async Task The_failure_that_reaches_the_caller_still_carries_the_original_cause()
    {
        var cause = new CommunicationException("the connection dropped");
        var factory = FakeSoapClientFactory.Always(() => FakeSoapDemoSoap.Throwing(cause));

        var error = await Assert.ThrowsAsync<DirectoryUnavailableException>(
            () => Directory(factory).FindByIdAsync("1", CancellationToken.None));

        Assert.Same(cause, error.InnerException);
    }

    [Fact]
    public async Task Nothing_is_cached_so_a_second_question_reaches_the_catalogue_again()
    {
        var person = SoapFixtures.LoadPerson(FindPersonFixture);
        var factory = FakeSoapClientFactory.Always(() => FakeSoapDemoSoap.Returning(person));
        var directory = Directory(factory);

        await directory.FindByIdAsync("1", CancellationToken.None);
        await directory.FindByIdAsync("1", CancellationToken.None);

        Assert.Equal(2, factory.Attempts);
        Assert.All(factory.Created, client => Assert.True(client.WasClosed));
    }

    private static SoapCustomerDirectory Directory(FakeSoapClientFactory factory) => new(factory.Create);
}
