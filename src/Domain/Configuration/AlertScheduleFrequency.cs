namespace ErpApp.Domain.Configuration;

/// <summary>
/// Recurrence of a scheduled alert. Exactly one member, live-confirmed the same way
/// <see cref="AlertMedium"/> was: the reference product's Schedule dropdown offers "Daily" alone,
/// paired with a single time-of-day picker -- there is no weekly/monthly option and no cron
/// expression anywhere on the screen.
///
/// This is load-bearing for the scheduler's design: "one time-of-day per definition, once per
/// local calendar day" is a far smaller problem than a general recurrence rule, and Phase 20e
/// deliberately built the small one (see docs/phase-20e-status.md, Decision A).
/// </summary>
public enum AlertScheduleFrequency
{
    Daily,
}
