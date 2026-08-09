namespace Campaign.Core.Ports;

/// <summary>
/// The customer catalogue could not answer. Every way the adapter can fail - a timeout, a broken
/// connection, a SOAP fault - leaves through this one exception, so neither the use cases nor the web
/// layer have to know what technology sits behind the port. A grant is never created on this path:
/// there is no "waiting to be checked" state.
/// </summary>
public sealed class DirectoryUnavailableException : Exception
{
    public DirectoryUnavailableException(string message)
        : base(message)
    {
    }

    public DirectoryUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
