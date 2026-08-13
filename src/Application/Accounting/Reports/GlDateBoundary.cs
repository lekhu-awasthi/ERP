namespace ErpApp.Application.Accounting.Reports;

/// <summary>
/// Converts a report's DateOnly cutoff/range into the UTC DateTimeOffset boundaries GlJournalEntry
/// .PostedAt (a DateTimeOffset stamped via DateTimeOffset.UtcNow at Approve time) is compared
/// against. There's no per-tenant timezone concept anywhere in this codebase yet -- every posting
/// timestamp is UTC -- so "AsOfDate end of day" is deliberately UTC end-of-day, not the browser's
/// local end-of-day; see phase-8a-status.md's scope-decision section for the same reasoning
/// applied to "which timestamp a report cuts off on" more broadly.
/// </summary>
internal static class GlDateBoundary
{
    public static DateTimeOffset EndOfDayUtc(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

    public static DateTimeOffset StartOfDayUtc(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
