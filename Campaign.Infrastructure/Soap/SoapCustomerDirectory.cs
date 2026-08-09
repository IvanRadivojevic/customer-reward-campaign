namespace Campaign.Infrastructure.Soap;

using System.ServiceModel;
using Campaign.Core.Ports;
using Campaign.Infrastructure.Soap.Generated;
using Polly;
using Polly.Retry;
using Polly.Timeout;

/// <summary>
/// Reads customers from the external SOAP catalogue through the client generated from the local
/// WSDL. Nothing is cached: the catalogue is somebody else's data, and a stale name copied onto a
/// grant would quietly break P-07, which promises the name was true at the moment of the grant.
/// </summary>
public sealed class SoapCustomerDirectory : ICustomerDirectory
{
    /// <summary>
    /// Five seconds per attempt. The generated WCF methods take no cancellation token, so this value
    /// is also put on the channel binding when the client is built; the strategy below is the outer
    /// guard and the binding is what actually cuts a hung call.
    /// </summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<SOAPDemoSoap> _clientFactory;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>
    /// Takes a factory rather than a client, because a WCF channel does not survive its first
    /// failure: a timeout or a dropped connection moves it to Faulted and every later call on it
    /// throws instead of reaching the service. Retrying on the same instance would spend the whole
    /// budget on a channel that can no longer talk to anybody.
    /// </summary>
    public SoapCustomerDirectory(Func<SOAPDemoSoap> clientFactory)
    {
        _clientFactory = clientFactory;
        _pipeline = BuildPipeline();
    }

    public async Task<CustomerDto?> FindByIdAsync(string externalCustomerId, CancellationToken ct)
    {
        var person = await CallAsync(
            client => client.FindPersonAsync(externalCustomerId),
            $"looking up customer '{externalCustomerId}'",
            ct);

        // The Person the service returns carries no id of its own, so the external id can only be
        // the one that was asked for.
        return person is null || string.IsNullOrWhiteSpace(person.Name)
            ? null
            : new CustomerDto(externalCustomerId, person.Name);
    }

    public async Task<IReadOnlyList<CustomerDto>> SearchByNameAsync(string name, CancellationToken ct)
    {
        var matches = await CallAsync(
            client => client.GetListByNameAsync(name),
            $"searching customers by name '{name}'",
            ct);

        if (matches is null)
        {
            return [];
        }

        return matches
            .Where(match => !string.IsNullOrWhiteSpace(match.ID) && !string.IsNullOrWhiteSpace(match.Name))
            .Select(match => new CustomerDto(match.ID, match.Name))
            .ToList();
    }

    private async Task<T?> CallAsync<T>(Func<SOAPDemoSoap, Task<T>> call, string what, CancellationToken ct)
        where T : class
    {
        try
        {
            return await _pipeline.ExecuteAsync(async _ => await AttemptAsync(call), ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new DirectoryUnavailableException($"The customer catalogue failed while {what}.", exception);
        }
    }

    /// <summary>One attempt gets one channel, and that channel is disposed of before the attempt ends.</summary>
    private async Task<T> AttemptAsync<T>(Func<SOAPDemoSoap, Task<T>> call)
    {
        var client = _clientFactory();

        try
        {
            var result = await call(client);
            Close(client);
            return result;
        }
        catch
        {
            // Abort, never Close: closing a faulted channel throws a second exception and would
            // replace the real reason the call failed.
            Abort(client);
            throw;
        }
    }

    private static void Close(SOAPDemoSoap client)
    {
        if (client is not ICommunicationObject channel)
        {
            return;
        }

        try
        {
            channel.Close();
        }
        catch (Exception exception) when (exception is CommunicationException or TimeoutException)
        {
            // The channel broke while being closed; there is nothing left to do but drop it.
            channel.Abort();
        }
    }

    private static void Abort(SOAPDemoSoap client)
    {
        if (client is ICommunicationObject channel)
        {
            channel.Abort();
        }
    }

    private static ResiliencePipeline BuildPipeline() =>
        new ResiliencePipelineBuilder()
            // Retry is added first, so it sits outside the timeout and the five seconds are counted
            // per attempt rather than for all three together.
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(250),
                UseJitter = true,
                ShouldHandle = arguments => ValueTask.FromResult(IsWorthRetrying(arguments.Outcome.Exception))
            })
            .AddTimeout(AttemptTimeout)
            .Build();

    /// <summary>
    /// A dropped connection or a timeout may well succeed on the next attempt. A SOAP fault is the
    /// service answering, not failing to answer, so repeating the same question is pointless.
    /// </summary>
    private static bool IsWorthRetrying(Exception? exception) => exception switch
    {
        FaultException => false,
        TimeoutRejectedException or CommunicationException or TimeoutException => true,
        _ => false
    };
}
