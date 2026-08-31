namespace ErpApp.Domain.Common;

/// <summary>
/// The single place this codebase converts between UTC and the tenant's wall clock.
///
/// <para>This product is Nepal-only by design (NPR, Nepal IRD TDS types, Bikram-Sambat fiscal
/// years, a Nepali-SME product brief), and <see cref="ErpApp.Domain.Tenancy.Organization"/> carries
/// no timezone field -- so there is exactly one tenant timezone, and it belongs here rather than as
/// a per-tenant setting nobody would ever change.</para>
///
/// <para><b>Why a fixed offset and not TimeZoneInfo:</b> Nepal has observed UTC+05:45 continuously
/// since 1986 and has never observed DST, so there is no rule for a tz database to add value over.
/// A fixed offset is also identical on Windows and Linux, whereas the id differs
/// ("Nepal Standard Time" vs "Asia/Kathmandu") and a FindSystemTimeZoneById miss throws at runtime
/// on whichever platform was not the one it was written on. The :45 offset is one of the most
/// commonly mishandled in the world; centralising it in one constant is the point of this type.</para>
/// </summary>
public static class NepalTime
{
    /// <summary>UTC+05:45.</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromMinutes(345);

    /// <summary>The same instant, expressed on Nepal's wall clock.</summary>
    public static DateTimeOffset ToLocal(DateTimeOffset instant) => instant.ToOffset(Offset);

    /// <summary>The local calendar date an instant falls on.</summary>
    public static DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(ToLocal(instant).DateTime);

    /// <summary>The local wall-clock time of day an instant falls on, truncated to whole minutes
    /// (the resolution the alert time picker and <see cref="ErpApp.Domain.Configuration.AlertDefinition.ScheduleTime"/>
    /// both work in).</summary>
    public static TimeOnly LocalTimeOfDay(DateTimeOffset instant)
    {
        var local = ToLocal(instant);
        return new TimeOnly(local.Hour, local.Minute);
    }
}
