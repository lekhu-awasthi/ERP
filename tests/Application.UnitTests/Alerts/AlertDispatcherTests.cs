using ErpApp.Application.Alerts;
using ErpApp.Application.Common.Email;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ErpApp.Application.UnitTests.Alerts;

/// <summary>
/// The scheduler's behavioural suite. Every test drives <see cref="AlertDispatcher"/> directly with
/// a <see cref="FakeTimeProvider"/>; there is no Task.Delay, no Thread.Sleep and no real clock
/// anywhere, which is the whole reason the timer lives in the hosted service and the decisions live
/// here (see IAlertDispatcher's remarks, and docs/phase-19-status.md bug #2 for the real-clock trap
/// this codebase has already been burned by once).
///
/// <para><b>Note on what the InMemory provider cannot prove.</b> It does not enforce unique indexes,
/// so the "two instances race and the loser's insert is rejected" path in
/// AlertDispatcher.TryClaimAsync cannot be exercised here. The first-line defence -- the
/// already-claimed pre-check -- is fully covered below and is what protects the single-instance
/// case, including across restarts; the unique index is the second line and is asserted in the
/// migration and verified against real SQL Server during manual E2E. See
/// docs/phase-20e-status.md's testing section.</para>
/// </summary>
public class AlertDispatcherTests
{
    // 2026-06-15 10:00 UTC == 15:45 Nepal (UTC+05:45). Every fixed instant below is written UTC-first
    // and its Nepal equivalent stated, so a test can never accidentally pass by reading UTC as local.
    private static readonly DateTimeOffset TenAmUtc = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fires_once_the_local_clock_has_passed_the_scheduled_time()
    {
        var (db, sender, clock, dispatcher) = CreateDispatcher(TenAmUtc);
        var organizationId = await SeedOrganizationAsync(db, "Acme Traders");
        await SeedAlertAsync(db, organizationId, "Daily", new TimeOnly(15, 30), clock);

        var sent = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(1, sent);
        Assert.Single(sender.SentEmails);
        Assert.Equal("ops@acme.test", sender.SentEmails[0].To);
    }

    [Fact]
    public async Task Does_not_fire_before_the_scheduled_local_time()
    {
        // 15:45 Nepal; the alert is set for 16:00 Nepal, so it is not yet due.
        var (db, sender, clock, dispatcher) = CreateDispatcher(TenAmUtc);
        var organizationId = await SeedOrganizationAsync(db, "Acme Traders");
        await SeedAlertAsync(db, organizationId, "Daily", new TimeOnly(16, 0), clock);

        var sent = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Empty(sender.SentEmails);
        Assert.Empty(await db.AlertSendLogs.ToListAsync());
    }

    /// <summary>
    /// The assertion that fails under a naive UTC implementation, and the reason it exists.
    ///
    /// <para>At 2026-06-15 <b>18:30 UTC</b> the Nepal wall clock reads <b>00:15 on 2026-06-16</b> --
    /// a different calendar day and a time-of-day of 00:15, not 18:30. An alert scheduled for 20:00
    /// must therefore <i>not</i> fire: 20:00 has not yet arrived on the new local day. A UTC-based
    /// implementation would compare 18:30 &gt;= 20:00, correctly not fire, and pass by luck -- so the
    /// second half of the test is what actually discriminates: an alert at 00:10 local <i>must</i>
    /// fire, and it is logged against 2026-06-16, the local date, while UTC still says the 15th.</para>
    /// </summary>
    [Fact]
    public async Task Uses_the_Nepal_local_day_and_time_not_UTC()
    {
        var justAfterLocalMidnight = new DateTimeOffset(2026, 6, 15, 18, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 6, 16), NepalTime.LocalDate(justAfterLocalMidnight));
        Assert.Equal(new TimeOnly(0, 15), NepalTime.LocalTimeOfDay(justAfterLocalMidnight));

        var (db, sender, clock, dispatcher) = CreateDispatcher(justAfterLocalMidnight);
        var organizationId = await SeedOrganizationAsync(db, "Acme Traders");
        await SeedAlertAsync(db, organizationId, "Evening", new TimeOnly(20, 0), clock);
        await SeedAlertAsync(db, organizationId, "Just after midnight", new TimeOnly(0, 10), clock);

        var sent = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(1, sent);
        Assert.Single(sender.SentEmails);

        var log = await db.AlertSendLogs.SingleAsync();
        Assert.Equal(new DateOnly(2026, 6, 16), log.OccurrenceDate);
        Assert.NotEqual(DateOnly.FromDateTime(justAfterLocalMidnight.UtcDateTime), log.OccurrenceDate);
    }

    [Fact]
    public async Task Does_not_fire_twice_in_the_same_local_day()
    {
        var (db, sender, clock, dispatcher) = CreateDispatcher(TenAmUtc);
        var organizationId = await SeedOrganizationAsync(db, "Acme Traders");
        await SeedAlertAsync(db, organizationId, "Daily", new TimeOnly(15, 30), clock);

        Assert.Equal(1, await dispatcher.DispatchDueAsync(CancellationToken.None));

        // Several more ticks across the rest of the local day.
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, await dispatcher.DispatchDueAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromHours(4));
        Assert.Equal(0, await dispatcher.DispatchDueAsync(CancellationToken.None));

        Assert.Single(sender.SentEmails);
        Assert.Single(await db.AlertSendLogs.ToListAsync());
    }

    [Fact]
    public async Task Fires_again_on_the_next_local_day()
    {
        var (db, sender, clock, dispatcher) = CreateDispatcher(TenAmUtc);
        var organizationId = await SeedOrganizationAsync(db, "Acme Traders");
        await SeedAlertAsync(db, organizationId, "Daily", new TimeOnly(15, 30), clock);

        await dispatcher.DispatchDueAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(1));
        await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(2, sender.SentEmails.Count);

        var occurrences = await db.AlertSendLogs.Select(l => l.OccurrenceDate).OrderBy(d => d).ToListAsync();
        Assert.Equal([new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 16)], occurrences);
    }

    /// <summary>
    /// Decision C's catch-up rule, both halves. A slot missed earlier <i>today</i> still fires when
    /// the process comes back; days that elapsed entirely while it was down are never revisited, so
    /// a three-day outage cannot produce a three-day mail backlog on restart.
    /// </summary>
    [Fact]
    public async Task Fires_a_missed_slot_later_the_same_day_but_never_backfills_earlier_days()
    {
        var (db, sender, clock, dispatcher) = CreateDispatcher(TenAmUtc);
        var organizationId = await SeedOrganizationAsync(db, "Acme Traders");
        await SeedAlertAsync(db, organizationId, "Morning", new TimeOnly(6, 0), clock);

        // 15:45 local, hours after the 06:00 slot: the missed occurrence still fires.
        Assert.Equal(1, await dispatcher.DispatchDueAsync(CancellationToken.None));

        // Now simulate a three-day outage.
        clock.Advance(TimeSpan.FromDays(3));
        Assert.Equal(1, await dispatcher.DispatchDueAsync(CancellationToken.None));

        Assert.Equal(2, sender.SentEmails.Count);
        var occurrences = await db.AlertSendLogs.Select(l => l.OccurrenceDate).OrderBy(d => d).ToListAsync();
        Assert.Equal([new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 18)], occurrences);
    }

    /// <summary>
    /// The test that protects real recipients from duplicate mail: tick, throw the dispatcher away
    /// entirely and build a new one over the same database (a process restart), tick again.
    /// </summary>
    [Fact]
    public async Task Sends_exactly_once_across_a_simulated_process_restart()
    {
        var db = TestAppDbContext.Create();
        var clock = new FakeTimeProvider(TenAmUtc);
        var organizationId = await SeedOrganizationAsync(db, "Acme Traders");
        await SeedAlertAsync(db, organizationId, "Daily", new TimeOnly(15, 30), clock);

        var firstRun = new FakeEmailSender();
        await Build(db, firstRun, clock).DispatchDueAsync(CancellationToken.None);

        // A brand-new dispatcher and sender, same database -- the ledger row is the only thing that
        // survives, and it is what has to prevent the second send.
        var secondRun = new FakeEmailSender();
        clock.Advance(TimeSpan.FromMinutes(5));
        var sentAfterRestart = await Build(db, secondRun, clock).DispatchDueAsync(CancellationToken.None);

        Assert.Single(firstRun.SentEmails);
        Assert.Equal(0, sentAfterRestart);
        Assert.Empty(secondRun.SentEmails);
    }

    [Fact]
    public async Task Skips_inactive_definitions()
    {
        var (db, sender, clock, dispatcher) = CreateDispatcher(TenAmUtc);
        var organizationId = await SeedOrganizationAsync(db, "Acme Traders");
        var alert = await SeedAlertAsync(db, organizationId, "Daily", new TimeOnly(15, 30), clock);
        alert.SetActive(false);
        await db.SaveChangesAsync();

        Assert.Equal(0, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Empty(sender.SentEmails);
    }

    /// <summary>
    /// Tenant isolation, at the one place in the codebase that deliberately queries across tenants.
    /// Two organizations, two alerts, and each recipient must receive their own organization's name
    /// -- not just "two emails went out".
    /// </summary>
    [Fact]
    public async Task Each_tenants_alert_carries_only_that_tenants_data()
    {
        var (db, sender, clock, dispatcher) = CreateDispatcher(TenAmUtc);
        var acme = await SeedOrganizationAsync(db, "Acme Traders");
        var globex = await SeedOrganizationAsync(db, "Globex Supplies");
        await SeedAlertAsync(db, acme, "Acme daily", new TimeOnly(15, 30), clock, "acme@test.example");
        await SeedAlertAsync(db, globex, "Globex daily", new TimeOnly(15, 30), clock, "globex@test.example");

        await dispatcher.DispatchDueAsync(CancellationToken.None);

        var toAcme = sender.SentEmails.Single(e => e.To == "acme@test.example");
        var toGlobex = sender.SentEmails.Single(e => e.To == "globex@test.example");

        Assert.Contains("Acme Traders", toAcme.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Globex Supplies", toAcme.Body, StringComparison.Ordinal);
        Assert.Contains("Globex Supplies", toGlobex.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme Traders", toGlobex.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sends_one_email_per_recipient_and_logs_each_separately()
    {
        var (db, sender, clock, dispatcher) = CreateDispatcher(TenAmUtc);
        var organizationId = await SeedOrganizationAsync(db, "Acme Traders");
        await SeedAlertAsync(
            db, organizationId, "Daily", new TimeOnly(15, 30), clock, "a@test.example, b@test.example");

        var sent = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(2, sent);
        Assert.Equal(
            ["a@test.example", "b@test.example"],
            sender.SentEmails.Select(e => e.To).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(2, await db.AlertSendLogs.CountAsync());
    }

    /// <summary>A failed send is recorded, not retried, and does not stop the other recipients.</summary>
    [Fact]
    public async Task Records_a_failed_send_and_does_not_retry_it_within_the_occurrence()
    {
        var db = TestAppDbContext.Create();
        var clock = new FakeTimeProvider(TenAmUtc);
        var organizationId = await SeedOrganizationAsync(db, "Acme Traders");
        await SeedAlertAsync(
            db, organizationId, "Daily", new TimeOnly(15, 30), clock, "bad@test.example, good@test.example");

        var sender = new ThrowingEmailSender("bad@test.example");
        var dispatcher = Build(db, sender, clock);

        var sent = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(1, sent);
        Assert.Equal(["good@test.example"], sender.SentEmails.Select(e => e.To));

        var failed = await db.AlertSendLogs.SingleAsync(l => l.Recipient == "bad@test.example");
        Assert.Equal(AlertSendStatus.Failed, failed.Status);
        Assert.Contains("SMTP refused", failed.FailureReason!, StringComparison.Ordinal);

        // A later tick the same day must not have another go at the failed recipient.
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(0, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Single(sender.SentEmails);
    }

    /// <summary>A tenant with no activity gets a zero-figure summary, not a skipped send -- a daily
    /// summary that silently stops arriving is indistinguishable from a broken scheduler.</summary>
    [Fact]
    public async Task Sends_a_zero_summary_when_the_tenant_had_no_activity()
    {
        var (db, sender, clock, dispatcher) = CreateDispatcher(TenAmUtc);
        var organizationId = await SeedOrganizationAsync(db, "Quiet Traders");
        await SeedAlertAsync(db, organizationId, "Daily", new TimeOnly(15, 30), clock);

        Assert.Equal(1, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Contains("Sales Invoices", sender.SentEmails[0].Body, StringComparison.Ordinal);
        Assert.Contains("0.00", sender.SentEmails[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Records_a_content_build_failure_rather_than_retrying_it_on_every_tick()
    {
        var db = TestAppDbContext.Create();
        var clock = new FakeTimeProvider(TenAmUtc);
        var organizationId = await SeedOrganizationAsync(db, "Acme Traders");
        await SeedAlertAsync(db, organizationId, "Daily", new TimeOnly(15, 30), clock);

        var sender = new FakeEmailSender();
        var dispatcher = new AlertDispatcher(
            db, sender, [new ThrowingContentBuilder()], clock, NullLogger<AlertDispatcher>.Instance);

        Assert.Equal(0, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Empty(sender.SentEmails);

        var log = await db.AlertSendLogs.SingleAsync();
        Assert.Equal(AlertSendStatus.Failed, log.Status);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Single(await db.AlertSendLogs.ToListAsync());
    }

    [Fact]
    public async Task Resolves_the_content_builder_matching_the_alert_type()
    {
        var (db, sender, clock, dispatcher) = CreateDispatcher(TenAmUtc);
        var organizationId = await SeedOrganizationAsync(db, "Acme Traders");
        await SeedAlertAsync(
            db, organizationId, "CRM", new TimeOnly(15, 30), clock, alertType: AlertType.CrmReport);

        await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Contains("CRM Report", sender.SentEmails[0].Subject, StringComparison.Ordinal);
        Assert.Contains("Open pipeline", sender.SentEmails[0].Body, StringComparison.Ordinal);
    }

    private static (IAppDbContext Db, FakeEmailSender Sender, FakeTimeProvider Clock, AlertDispatcher Dispatcher)
        CreateDispatcher(DateTimeOffset nowUtc)
    {
        var db = TestAppDbContext.Create();
        var sender = new FakeEmailSender();
        var clock = new FakeTimeProvider(nowUtc);
        return (db, sender, clock, Build(db, sender, clock));
    }

    private static AlertDispatcher Build(IAppDbContext db, IEmailSender sender, TimeProvider clock) =>
        new(db,
            sender,
            [new DailyTransactionSummaryContentBuilder(db), new CrmReportContentBuilder(db)],
            clock,
            NullLogger<AlertDispatcher>.Instance);

    private static async Task<Guid> SeedOrganizationAsync(IAppDbContext db, string name)
    {
        var organization = ErpApp.Domain.Tenancy.Organization.Create(
            name, "Trading", null, new DateOnly(2026, 4, 1), true,
            $"ws-{Guid.NewGuid():N}", null, null, null, null, Guid.NewGuid());

        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        return organization.Id;
    }

    private static async Task<AlertDefinition> SeedAlertAsync(
        IAppDbContext db,
        Guid organizationId,
        string name,
        TimeOnly scheduleTime,
        FakeTimeProvider clock,
        string recipients = "ops@acme.test",
        AlertType alertType = AlertType.DailyTransactionSummary)
    {
        var alert = AlertDefinition.Create(
            organizationId, name, AlertMedium.Email, alertType, recipients,
            AlertScheduleFrequency.Daily, scheduleTime, Guid.NewGuid());

        db.AlertDefinitions.Add(alert);
        await db.SaveChangesAsync();

        // AlertDefinition.CreatedAt is stamped from the real clock (as every aggregate in this
        // codebase is); nothing in the dispatcher reads it except for ordering, so the fake clock
        // deliberately is not threaded into the factory just for that.
        _ = clock;
        return alert;
    }

    private sealed class ThrowingEmailSender(string failingRecipient) : IEmailSender
    {
        public List<(string To, string Subject, string Body)> SentEmails { get; } = [];

        public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (string.Equals(toEmail, failingRecipient, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SMTP refused {toEmail}.");
            }

            SentEmails.Add((toEmail, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingContentBuilder : IAlertContentBuilder
    {
        public AlertType AlertType => AlertType.DailyTransactionSummary;

        public Task<AlertContent> BuildAsync(
            Guid organizationId, DateOnly occurrenceDate, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Content build blew up.");
    }
}
