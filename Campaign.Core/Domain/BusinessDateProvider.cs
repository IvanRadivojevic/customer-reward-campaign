namespace Campaign.Core.Domain;

/// <summary>
/// Turns the current instant into the calendar date that carries business meaning. The time zone is
/// injected (Campaign:TimeZoneId in configuration, Europe/Belgrade by default) and the clock comes
/// from an injected TimeProvider, so nothing here reads the machine clock or hard-codes a zone.
/// A grant created at 00:30 in Belgrade belongs to the new day even though it is still the previous
/// day in UTC - which is exactly what the daily limit has to count.
/// </summary>
public sealed class BusinessDateProvider
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;

    public BusinessDateProvider(TimeProvider timeProvider, string timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        _timeProvider = timeProvider;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    /// <summary>Timestamps are stored in UTC; only calendar dates are converted to the business zone.</summary>
    public DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();

    public DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow(), _timeZone).DateTime);
}
