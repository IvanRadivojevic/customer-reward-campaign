namespace Campaign.Tests.Domain;

using Campaign.Core.Domain;
using Campaign.Tests.Fakes;

public class BusinessDateProviderTests
{
    private const string Belgrade = "Europe/Belgrade";

    [Fact]
    public void P01_business_date_rolls_over_at_local_midnight_and_not_at_utc_midnight()
    {
        // Summer time: Belgrade is two hours ahead of UTC.
        var justBefore = Provider(new DateTimeOffset(2026, 8, 9, 21, 59, 0, TimeSpan.Zero));
        var justAfter = Provider(new DateTimeOffset(2026, 8, 9, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 8, 9), justBefore.Today());
        Assert.Equal(new DateOnly(2026, 8, 10), justAfter.Today());
    }

    [Fact]
    public void P01_business_date_follows_the_offset_that_is_in_force_on_that_date()
    {
        // Winter time: the same rule, one hour ahead of UTC, without any of it being hard-coded.
        var justBefore = Provider(new DateTimeOffset(2026, 1, 15, 22, 59, 0, TimeSpan.Zero));
        var justAfter = Provider(new DateTimeOffset(2026, 1, 15, 23, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 1, 15), justBefore.Today());
        Assert.Equal(new DateOnly(2026, 1, 16), justAfter.Today());
    }

    [Fact]
    public void P01_a_different_configured_time_zone_gives_a_different_business_date()
    {
        var instant = new DateTimeOffset(2026, 8, 9, 22, 30, 0, TimeSpan.Zero);
        var belgrade = new BusinessDateProvider(new FixedTimeProvider(instant), Belgrade);
        var utc = new BusinessDateProvider(new FixedTimeProvider(instant), "UTC");

        Assert.Equal(new DateOnly(2026, 8, 10), belgrade.Today());
        Assert.Equal(new DateOnly(2026, 8, 9), utc.Today());
    }

    [Fact]
    public void P01_timestamps_stay_in_utc()
    {
        var instant = new DateTimeOffset(2026, 8, 9, 22, 30, 0, TimeSpan.Zero);

        Assert.Equal(instant, Provider(instant).UtcNow());
    }

    private static BusinessDateProvider Provider(DateTimeOffset utcNow) =>
        new(new FixedTimeProvider(utcNow), Belgrade);
}
