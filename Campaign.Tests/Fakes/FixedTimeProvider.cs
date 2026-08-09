namespace Campaign.Tests.Fakes;

/// <summary>
/// A clock the test controls. Written by hand rather than taken from a testing package, because
/// TimeProvider only has to answer one question here.
/// </summary>
public sealed class FixedTimeProvider : TimeProvider
{
    public FixedTimeProvider(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
