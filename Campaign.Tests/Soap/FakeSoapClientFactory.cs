namespace Campaign.Tests.Soap;

using Campaign.Infrastructure.Soap.Generated;

/// <summary>
/// Hands the adapter a fresh client for every attempt and keeps every one it handed out, so a test
/// can check not only what came back but which channel each attempt used and how it was disposed of.
/// </summary>
internal sealed class FakeSoapClientFactory
{
    private readonly Func<int, FakeSoapDemoSoap> _create;

    private FakeSoapClientFactory(Func<int, FakeSoapDemoSoap> create)
    {
        _create = create;
    }

    public List<FakeSoapDemoSoap> Created { get; } = [];

    public int Attempts => Created.Count;

    public int TotalCalls => Created.Sum(client => client.Calls);

    /// <summary>Every attempt gets an identically behaving, but brand new, client.</summary>
    public static FakeSoapClientFactory Always(Func<FakeSoapDemoSoap> create) => new(_ => create());

    /// <summary>The behaviour depends on which attempt this is, counted from zero.</summary>
    public static FakeSoapClientFactory PerAttempt(Func<int, FakeSoapDemoSoap> create) => new(create);

    public SOAPDemoSoap Create()
    {
        var client = _create(Created.Count);
        Created.Add(client);
        return client;
    }
}
