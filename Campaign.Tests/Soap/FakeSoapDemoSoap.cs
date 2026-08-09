namespace Campaign.Tests.Soap;

using System.ServiceModel;
using Campaign.Infrastructure.Soap.Generated;

/// <summary>
/// A stand-in for the generated SOAP contract. The tests never open a socket; what is being tested
/// is how the adapter maps the answers, what it does when the catalogue misbehaves, and how it
/// treats the channel afterwards.
/// </summary>
/// <remarks>
/// It implements <see cref="ICommunicationObject"/> the way a real WCF channel behaves: a failed
/// call leaves it Faulted, and closing a faulted channel throws. An adapter that closed a broken
/// channel instead of aborting it would therefore fail these tests rather than quietly work.
/// </remarks>
internal sealed class FakeSoapDemoSoap : SOAPDemoSoap, ICommunicationObject
{
    private readonly Func<string, Task<Person>>? _findPerson;
    private readonly Func<string, Task<PersonIdentification[]>>? _getListByName;

    private FakeSoapDemoSoap(
        Func<string, Task<Person>>? findPerson = null,
        Func<string, Task<PersonIdentification[]>>? getListByName = null)
    {
        _findPerson = findPerson;
        _getListByName = getListByName;
    }

    public int Calls { get; private set; }

    public bool WasClosed { get; private set; }

    public bool WasAborted { get; private set; }

    public CommunicationState State { get; private set; } = CommunicationState.Opened;

    public static FakeSoapDemoSoap Returning(Person person) =>
        new(findPerson: _ => Task.FromResult(person));

    public static FakeSoapDemoSoap Returning(PersonIdentification[] rows) =>
        new(getListByName: _ => Task.FromResult(rows));

    public static FakeSoapDemoSoap Throwing(Exception exception) =>
        new(
            findPerson: _ => Task.FromException<Person>(exception),
            getListByName: _ => Task.FromException<PersonIdentification[]>(exception));

    public Task<Person> FindPersonAsync(string id)
    {
        Calls++;
        return Record(_findPerson is null
            ? throw new NotSupportedException("This fake was not set up for FindPerson.")
            : _findPerson(id));
    }

    public Task<PersonIdentification[]> GetListByNameAsync(string name)
    {
        Calls++;
        return Record(_getListByName is null
            ? throw new NotSupportedException("This fake was not set up for GetListByName.")
            : _getListByName(name));
    }

    public void Abort()
    {
        WasAborted = true;
        State = CommunicationState.Closed;
    }

    public void Close()
    {
        if (State == CommunicationState.Faulted)
        {
            throw new CommunicationObjectFaultedException("The channel is faulted and cannot be closed.");
        }

        WasClosed = true;
        State = CommunicationState.Closed;
    }

    public void Close(TimeSpan timeout) => Close();

    public void Open() => State = CommunicationState.Opened;

    public void Open(TimeSpan timeout) => Open();

    // A failed call breaks the channel, exactly as it does in WCF.
    private async Task<T> Record<T>(Task<T> call)
    {
        try
        {
            return await call;
        }
        catch
        {
            State = CommunicationState.Faulted;
            throw;
        }
    }

    // The catalogue offers more than the two operations this solution uses. They are part of the
    // generated contract, so they have to exist here, but nothing may call them.
    public Task<long> AddIntegerAsync(long arg1, long arg2) => throw Unused();

    public Task<long> DivideIntegerAsync(long arg1, long arg2) => throw Unused();

    public Task<ArrayOfXElement> GetByNameAsync(string name) => throw Unused();

    public Task<System.Xml.XmlElement> GetDataSetByNameAsync(string name) => throw Unused();

    public Task<Address> LookupCityAsync(string zip) => throw Unused();

    public Task<string> MissionAsync() => throw Unused();

    public Task<System.Xml.XmlElement> QueryByNameAsync(string name) => throw Unused();

    // The asynchronous half of ICommunicationObject is never exercised by the adapter. Declaring the
    // events with empty accessors keeps them from becoming unused fields.
    public event EventHandler Closed { add { } remove { } }

    public event EventHandler Closing { add { } remove { } }

    public event EventHandler Faulted { add { } remove { } }

    public event EventHandler Opened { add { } remove { } }

    public event EventHandler Opening { add { } remove { } }

    public IAsyncResult BeginClose(AsyncCallback callback, object state) => throw Unused();

    public IAsyncResult BeginClose(TimeSpan timeout, AsyncCallback callback, object state) => throw Unused();

    public IAsyncResult BeginOpen(AsyncCallback callback, object state) => throw Unused();

    public IAsyncResult BeginOpen(TimeSpan timeout, AsyncCallback callback, object state) => throw Unused();

    public void EndClose(IAsyncResult result) => throw Unused();

    public void EndOpen(IAsyncResult result) => throw Unused();

    private static NotSupportedException Unused() =>
        new("This member is not used by the customer directory.");
}
