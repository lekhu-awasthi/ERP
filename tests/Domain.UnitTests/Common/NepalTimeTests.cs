using ErpApp.Domain.Common;

namespace ErpApp.Domain.UnitTests.Common;

/// <summary>
/// Nepal's UTC+05:45 is one of the most commonly mishandled offsets in the world, and every alert in
/// this product fires against it. These assertions are written so that a naive "local == UTC"
/// implementation fails all of them rather than passing by coincidence.
/// </summary>
public class NepalTimeTests
{
    [Fact]
    public void Offset_is_five_hours_forty_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(345), NepalTime.Offset);
    }

    [Fact]
    public void LocalTimeOfDay_adds_the_forty_five_minute_offset()
    {
        var instant = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal(new TimeOnly(15, 45), NepalTime.LocalTimeOfDay(instant));
    }

    /// <summary>The case that separates a local-date implementation from a UTC-date one: between
    /// 18:15 and 24:00 UTC, Nepal is already on the next calendar day.</summary>
    [Fact]
    public void LocalDate_rolls_over_before_UTC_midnight()
    {
        var lateEveningUtc = new DateTimeOffset(2026, 6, 15, 18, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 6, 16), NepalTime.LocalDate(lateEveningUtc));
        Assert.Equal(new DateOnly(2026, 6, 15), DateOnly.FromDateTime(lateEveningUtc.UtcDateTime));
        Assert.Equal(new TimeOnly(0, 15), NepalTime.LocalTimeOfDay(lateEveningUtc));
    }

    /// <summary>And the mirror case: early UTC morning is still the same Nepal day, so a naive
    /// implementation that subtracts instead of adding also fails.</summary>
    [Fact]
    public void LocalDate_does_not_roll_back_in_the_early_UTC_morning()
    {
        var earlyUtc = new DateTimeOffset(2026, 6, 15, 0, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 6, 15), NepalTime.LocalDate(earlyUtc));
        Assert.Equal(new TimeOnly(6, 15), NepalTime.LocalTimeOfDay(earlyUtc));
    }

    [Fact]
    public void LocalTimeOfDay_truncates_seconds_to_the_pickers_resolution()
    {
        var instant = new DateTimeOffset(2026, 6, 15, 10, 0, 59, TimeSpan.Zero);

        Assert.Equal(new TimeOnly(15, 45), NepalTime.LocalTimeOfDay(instant));
    }

    [Fact]
    public void ToLocal_preserves_the_instant()
    {
        var instant = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

        var local = NepalTime.ToLocal(instant);

        Assert.Equal(instant.UtcDateTime, local.UtcDateTime);
        Assert.Equal(NepalTime.Offset, local.Offset);
    }
}
