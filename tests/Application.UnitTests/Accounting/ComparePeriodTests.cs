using ErpApp.Application.Accounting.Reports;

namespace ErpApp.Application.UnitTests.Accounting;

/// <summary>
/// Phase 26a. The Compare column's whole correctness rests on which window it compares against, and
/// that decision lives in exactly one place -- so it is asserted directly here rather than only
/// through the three report handlers that consume it.
/// </summary>
public class ComparePeriodTests
{
    [Fact]
    public void SameLengthPrior_returns_the_window_of_equal_length_ending_the_day_before()
    {
        var (from, to) = ComparePeriod.SameLengthPrior(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28));

        Assert.Equal(new DateOnly(2026, 1, 4), from);
        Assert.Equal(new DateOnly(2026, 1, 31), to);
    }

    [Fact]
    public void SameLengthPrior_keeps_the_two_windows_exactly_the_same_number_of_days()
    {
        var fromDate = new DateOnly(2026, 3, 15);
        var toDate = new DateOnly(2026, 5, 2);
        var (compareFrom, compareTo) = ComparePeriod.SameLengthPrior(fromDate, toDate);

        Assert.Equal(toDate.DayNumber - fromDate.DayNumber, compareTo.DayNumber - compareFrom.DayNumber);
        Assert.Equal(fromDate.AddDays(-1), compareTo);
    }

    [Fact]
    public void SameLengthPrior_handles_a_single_day_period_as_the_day_before()
    {
        var day = new DateOnly(2026, 9, 2);

        var (from, to) = ComparePeriod.SameLengthPrior(day, day);

        Assert.Equal(new DateOnly(2026, 9, 1), from);
        Assert.Equal(new DateOnly(2026, 9, 1), to);
    }

    [Fact]
    public void PriorYearAsOf_returns_the_same_calendar_date_a_year_earlier()
    {
        Assert.Equal(new DateOnly(2025, 9, 2), ComparePeriod.PriorYearAsOf(new DateOnly(2026, 9, 2)));
    }

    [Fact]
    public void PriorYearAsOf_clamps_a_leap_day_to_the_28th_rather_than_returning_no_date()
    {
        // 2024 is a leap year, 2023 is not. Clamping is the conventional answer and, more to the
        // point, keeps the comparison a real date the report can label.
        Assert.Equal(new DateOnly(2023, 2, 28), ComparePeriod.PriorYearAsOf(new DateOnly(2024, 2, 29)));
    }
}
