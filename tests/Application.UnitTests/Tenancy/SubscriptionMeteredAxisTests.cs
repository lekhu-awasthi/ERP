using ErpApp.Application.Common.Behaviors;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Tenancy;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Application.Workflow;
using ErpApp.Application.Workflow.Commands.ExtractInboxDocument;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using ErpApp.Domain.Workflow;
using Microsoft.Extensions.Time.Testing;

namespace ErpApp.Application.UnitTests.Tenancy;

/// <summary>
/// Phase 46 -- the AI-scan ceiling, the allowance-year window that phase 41's whole-term window got
/// wrong, and the purchased-location count that is deliberately <b>not</b> a ceiling.
///
/// <para>The figures are the published ones (tiggapp.com/pricing, read 2026-09-15): 20 AI scans per
/// day on every tier, transactions stated per <i>year</i> on every tier and on every term length,
/// and billing locations sold at Rs 5,000 each with no in-product cap of any kind.</para>
/// </summary>
public class SubscriptionMeteredAxisTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    /// <summary>Mid-afternoon in Kathmandu, well clear of both midnights, so a test that is not
    /// about the day boundary cannot accidentally be about the day boundary.</summary>
    private static readonly DateTimeOffset Midday =
        new(2026, 9, 15, 12, 0, 0, NepalTime.Offset);

    // ---- The AI-scan ceiling ---------------------------------------------------------------

    [Fact]
    public async Task A_scan_is_allowed_below_the_daily_ceiling()
    {
        var db = TestAppDbContext.Create();
        await SeedAsync(db, dailyAiScanQuota: 3);
        await RecordScansAsync(db, 2, Midday);

        await ExtractAsync(db, Midday);
    }

    [Fact]
    public async Task A_scan_is_refused_once_the_daily_ceiling_is_spent()
    {
        var db = TestAppDbContext.Create();
        await SeedAsync(db, dailyAiScanQuota: 3);
        await RecordScansAsync(db, 3, Midday);

        var exception = await Assert.ThrowsAsync<SubscriptionQuotaExceededException>(
            () => ExtractAsync(db, Midday));

        Assert.Equal(SubscriptionQuotaKind.AiScans, exception.Kind);
        Assert.Equal(3, exception.Used);
        Assert.Equal(3, exception.Quota);
    }

    /// <summary>
    /// The scan ceiling is the one with no add-on behind it, so its message must not send the reader
    /// to buy capacity that is not sold. Every published tier grants the same 20.
    /// </summary>
    [Fact]
    public async Task The_scan_refusal_does_not_offer_an_upgrade_that_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        await SeedAsync(db, dailyAiScanQuota: 1);
        await RecordScansAsync(db, 1, Midday);

        var exception = await Assert.ThrowsAsync<SubscriptionQuotaExceededException>(
            () => ExtractAsync(db, Midday));

        Assert.Contains("refreshes at midnight", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("higher plan", exception.Message, StringComparison.Ordinal);
        Assert.Contains("1 AI scan per day", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole reason the count reads the audit trail rather than the document. A re-scan
    /// overwrites <c>UploadedDocument.ExtractionAttemptedAt</c>, so counting documents would charge
    /// one unit for ten runs against one bill -- and the ten runs are the ten API calls that
    /// actually cost money. Phase 26c's rule: derive a dated figure from the append-only history.
    /// </summary>
    [Fact]
    public async Task Re_scanning_one_document_spends_one_unit_each_time()
    {
        var db = TestAppDbContext.Create();
        await SeedAsync(db, dailyAiScanQuota: 2);

        // Three attempts, all against the SAME document id -- which is what a user clicking Extract
        // three times on one row produces.
        var documentId = Guid.NewGuid();
        await RecordScansAsync(db, 2, Midday, documentId);

        await Assert.ThrowsAsync<SubscriptionQuotaExceededException>(() => ExtractAsync(db, Midday));
    }

    /// <summary>
    /// The allowance is per <b>Nepal-local</b> day, not per UTC day. UTC midnight is 05:45 local, so
    /// a scan run at 05:00 local sits in the previous UTC day: counting on UTC would hand a tenant a
    /// second allowance every morning between midnight and 05:45, and would refuse them in the
    /// evening against scans that belong to a day already over. Phase 20e's rule -- test the
    /// after-local-midnight case, not just the evening-UTC one.
    /// </summary>
    [Fact]
    public async Task The_daily_allowance_turns_over_at_Nepal_midnight_not_UTC_midnight()
    {
        var db = TestAppDbContext.Create();
        await SeedAsync(db, dailyAiScanQuota: 2);

        // 23:30 Nepal on the 15th. In UTC this is 17:45 on the 15th.
        var lateOn15th = new DateTimeOffset(2026, 9, 15, 23, 30, 0, NepalTime.Offset);
        await RecordScansAsync(db, 2, lateOn15th);

        // Still the 15th locally -- refused.
        await Assert.ThrowsAsync<SubscriptionQuotaExceededException>(() => ExtractAsync(db, lateOn15th));

        // 00:30 Nepal on the 16th: a new local day, and the allowance is back. In UTC this is still
        // the 15th (18:45), so a UTC-keyed window would refuse here -- which is the bug under test.
        var earlyOn16th = new DateTimeOffset(2026, 9, 16, 0, 30, 0, NepalTime.Offset);
        await ExtractAsync(db, earlyOn16th);
    }

    /// <summary>Zero stays the not-metered sentinel on this axis too, so a tenant can be exempted --
    /// it is only the <i>default</i> that differs from the other two.</summary>
    [Fact]
    public async Task A_zero_scan_quota_is_not_metered()
    {
        var db = TestAppDbContext.Create();
        await SeedAsync(db, dailyAiScanQuota: 0);
        await RecordScansAsync(db, 50, Midday);

        await ExtractAsync(db, Midday);
    }

    /// <summary>One tenant's scans never count against another's.</summary>
    [Fact]
    public async Task Scan_usage_is_counted_per_tenant()
    {
        var db = TestAppDbContext.Create();
        await SeedAsync(db, dailyAiScanQuota: 2);

        // Two scans, but on somebody else's organization.
        await RecordScansAsync(db, 2, Midday, organizationId: Guid.NewGuid());

        await ExtractAsync(db, Midday);
    }

    /// <summary>
    /// Only extraction audit rows count. Every command in the product writes an audit row, so a
    /// count that forgot to filter by DocumentType would meter AI scans against invoice approvals --
    /// exhausting the day's allowance on a tenant that had never opened the inbox.
    /// </summary>
    [Fact]
    public async Task Ordinary_audited_actions_do_not_spend_the_scan_allowance()
    {
        var db = TestAppDbContext.Create();
        await SeedAsync(db, dailyAiScanQuota: 2);

        for (var i = 0; i < 20; i++)
        {
            db.Audits.Add(Audit.Create(
                OrganizationId, Guid.NewGuid(), "Approve", DocumentType.Invoice, Guid.NewGuid()));
        }

        await db.SaveChangesAsync(CancellationToken.None);

        await ExtractAsync(db, Midday);
    }

    // ---- The allowance year ----------------------------------------------------------------

    /// <summary>
    /// Phase 41 counted over the whole term. Every published quota is stated per year and the
    /// 3-year tab states the same figures as the 1-year tab beside a tripled price, so a term is a
    /// number of allowance years bought at once. Counting over the term would give a 3-year tenant
    /// one year's transactions to last three.
    /// </summary>
    [Fact]
    public void A_multi_year_term_meters_one_allowance_year_at_a_time()
    {
        var termStart = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var subscription = Subscription(termStart, termStart.AddYears(3));

        var firstYear = SubscriptionUsageReader.CurrentAllowanceYear(subscription, termStart.AddDays(10));
        Assert.Equal(termStart, firstYear.Start);
        Assert.Equal(termStart.AddYears(1), firstYear.End);

        var secondYear = SubscriptionUsageReader.CurrentAllowanceYear(subscription, termStart.AddYears(1).AddDays(10));
        Assert.Equal(termStart.AddYears(1), secondYear.Start);
        Assert.Equal(termStart.AddYears(2), secondYear.End);

        var thirdYear = SubscriptionUsageReader.CurrentAllowanceYear(subscription, termStart.AddYears(2).AddDays(10));
        Assert.Equal(termStart.AddYears(2), thirdYear.Start);
        Assert.Equal(termStart.AddYears(3), thirdYear.End);
    }

    /// <summary>A one-year term is one allowance year, so the fix changes nothing for the common
    /// case -- which is what makes it safe to apply to every existing tenant.</summary>
    [Fact]
    public void A_one_year_term_is_a_single_allowance_year()
    {
        var termStart = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var subscription = Subscription(termStart, termStart.AddYears(1));

        var (start, end) = SubscriptionUsageReader.CurrentAllowanceYear(subscription, termStart.AddDays(200));

        Assert.Equal(termStart, start);
        Assert.Equal(termStart.AddYears(1), end);
    }

    /// <summary>The window never runs past the term: a 15-day trial is a stub of an allowance year,
    /// not a year.</summary>
    [Fact]
    public void An_allowance_year_is_clamped_to_a_term_shorter_than_a_year()
    {
        // Anchored to now rather than to a fixed past date: SetPlan refuses a term ending before the
        // tenant existed, and CreateTrial stamps the origin from the real clock.
        var termStart = DateTimeOffset.UtcNow;
        var termEnd = termStart.AddDays(15);
        var subscription = Subscription(termStart, termEnd);

        var (start, end) = SubscriptionUsageReader.CurrentAllowanceYear(subscription, termStart.AddDays(3));

        Assert.Equal(termStart, start);
        Assert.Equal(termEnd, end);
    }

    /// <summary>
    /// Anniversaries come from <c>AddYears</c> rather than from a 365-day arithmetic, so a term
    /// beginning on 29 February lands on the 28th in common years instead of sliding by a day each
    /// year until it has drifted a week.
    /// </summary>
    [Fact]
    public void A_leap_day_term_keeps_its_anniversary()
    {
        var termStart = new DateTimeOffset(2028, 2, 29, 0, 0, 0, TimeSpan.Zero);
        var subscription = Subscription(termStart, termStart.AddYears(3));

        var secondYear = SubscriptionUsageReader.CurrentAllowanceYear(subscription, new DateTimeOffset(2029, 6, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2029, 2, 28, 0, 0, 0, TimeSpan.Zero), secondYear.Start);
    }

    /// <summary>
    /// The consequence that matters: a transaction posted in year one does not count against year
    /// two's allowance. Counted end to end through the reader rather than asserted on the window,
    /// because the window is only interesting if the count uses it.
    /// </summary>
    [Fact]
    public async Task Transactions_from_an_earlier_allowance_year_do_not_count_against_this_one()
    {
        var db = TestAppDbContext.Create();
        var termStart = DateTimeOffset.UtcNow.AddYears(-1).AddDays(-10);
        var subscription = await SeedAsync(
            db, dailyAiScanQuota: 0, transactionQuota: 5, termStart: termStart, termEnd: termStart.AddYears(3));

        // Three in the first allowance year, one in the current one.
        await PostEntryAsync(db, termStart.AddDays(1));
        await PostEntryAsync(db, termStart.AddDays(2));
        await PostEntryAsync(db, termStart.AddDays(3));
        await PostEntryAsync(db, DateTimeOffset.UtcNow.AddDays(-1));

        var used = await SubscriptionUsageReader.CountTransactionsAsync(
            db, subscription, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(1, used);
    }

    // ---- The purchased-location count ------------------------------------------------------

    /// <summary>
    /// Phase 46's centre of gravity, and the assertion that records it. The reference product runs
    /// Cadehi on three locations with Billing Location simply <i>Enabled</i>: the list is unbounded,
    /// the Add New Location dialog names no cap, no remaining count and no charge, and the
    /// Subscriptions screen shows no location row at all. So the purchased count is a record, and
    /// exceeding it refuses nothing. Phase 43's product-to-location precedent.
    /// </summary>
    [Fact]
    public async Task Holding_more_locations_than_were_purchased_is_reported_and_never_refused()
    {
        var db = TestAppDbContext.Create();
        var subscription = await SeedAsync(db, dailyAiScanQuota: 0, locationQuota: 1);

        for (var i = 0; i < 3; i++)
        {
            db.BillingLocations.Add(BillingLocation.Create(
                OrganizationId, $"L{i}", $"Location {i}", "Bhairahawa", null));
        }

        await db.SaveChangesAsync(CancellationToken.None);

        var usage = await SubscriptionUsageReader.ReadAsync(
            db, subscription, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(3, usage.LocationsUsed);
        Assert.Equal(1, usage.LocationQuota);
        Assert.True(usage.LocationsRecorded);

        // And the thing that makes this a record rather than a ceiling: nothing throws. There is no
        // SubscriptionQuotaKind for locations, by design.
        Assert.DoesNotContain(
            Enum.GetValues<SubscriptionQuotaKind>(),
            x => x.ToString().Contains("Location", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unrecorded_purchased_location_count_still_reports_what_is_in_use()
    {
        var db = TestAppDbContext.Create();
        var subscription = await SeedAsync(db, dailyAiScanQuota: 0, locationQuota: 0);

        db.BillingLocations.Add(BillingLocation.Create(OrganizationId, "HO", "HeadOffice", "Bhairahawa", null));
        await db.SaveChangesAsync(CancellationToken.None);

        var usage = await SubscriptionUsageReader.ReadAsync(
            db, subscription, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(1, usage.LocationsUsed);
        Assert.False(usage.LocationsRecorded);
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static TenantSubscription Subscription(DateTimeOffset termStart, DateTimeOffset termEnd)
    {
        var subscription = TenantSubscription.CreateTrial(OrganizationId, default);
        subscription.SetPlan(null, "Standard", termStart, termEnd, 20_000m, 0, 5, 20, 0, false);
        return subscription;
    }

    private static async Task<TenantSubscription> SeedAsync(
        IAppDbContext db,
        int dailyAiScanQuota,
        int transactionQuota = 0,
        int locationQuota = 0,
        DateTimeOffset? termStart = null,
        DateTimeOffset? termEnd = null)
    {
        var subscription = TenantSubscription.CreateTrial(OrganizationId, default);
        db.TenantSubscriptions.Add(subscription);
        await db.SaveChangesAsync(CancellationToken.None);

        var start = termStart ?? DateTimeOffset.UtcNow.AddYears(-1);
        subscription.SetPlan(
            null, "Standard", start, termEnd ?? start.AddYears(5),
            20_000m, 0, transactionQuota, dailyAiScanQuota, locationQuota, irdVerified: false);

        await db.SaveChangesAsync(CancellationToken.None);

        return subscription;
    }

    /// <summary>
    /// Writes the audit rows an extraction leaves behind. Deliberately the <i>real</i> shape
    /// <c>AuditBehavior</c> produces for <c>ExtractInboxDocumentCommand</c> -- action "Extract",
    /// <c>DocumentType.DocumentExtraction</c> -- because a count that agreed with a made-up shape
    /// would prove nothing about the count that runs in production.
    /// </summary>
    private static async Task RecordScansAsync(
        IAppDbContext db, int count, DateTimeOffset at, Guid? documentId = null, Guid? organizationId = null)
    {
        for (var i = 0; i < count; i++)
        {
            var audit = Audit.Create(
                organizationId ?? OrganizationId,
                Guid.NewGuid(),
                "Extract",
                DocumentType.DocumentExtraction,
                documentId ?? Guid.NewGuid());

            // Audit stamps CreatedAt itself, so the instant is forced through the tracker -- the same
            // accommodation phase 31 made to reach a state only time produces.
            db.Audits.Add(audit);
            await db.SaveChangesAsync(CancellationToken.None);

            db.Audits.Entry(audit).Property(nameof(Audit.CreatedAt)).CurrentValue = at;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    /// <summary>Runs the Extract command through the quota behavior alone and asserts it reached the
    /// handler.</summary>
    private static async Task ExtractAsync(IAppDbContext db, DateTimeOffset now)
    {
        var clock = new FakeTimeProvider(now);
        var behavior = new SubscriptionQuotaBehavior<ExtractInboxDocumentCommand, InboxDocumentDto>(db, clock);
        var expected = InboxDocumentStub();

        var actual = await behavior.Handle(
            new ExtractInboxDocumentCommand(OrganizationId, Guid.NewGuid()),
            () => Task.FromResult(expected),
            CancellationToken.None);

        Assert.Same(expected, actual);
    }

    private static InboxDocumentDto InboxDocumentStub()
    {
        return new InboxDocumentDto(
            Guid.NewGuid(), "bill.png", 10, "image/png", null, null,
            UploadedDocumentStatus.Pending, Guid.NewGuid(), "Tester", DateTimeOffset.UtcNow,
            false, null, null, null,
            DocumentExtractionStatus.Succeeded, null, null, null, true, null);
    }

    private static async Task PostEntryAsync(IAppDbContext db, DateTimeOffset postedAt)
    {
        var entry = ErpApp.Domain.Accounting.GlJournalEntry.Post(
            OrganizationId, DocumentType.Invoice, Guid.NewGuid(),
            [new ErpApp.Domain.Accounting.GlLineInput(Guid.NewGuid(), 10m, 0m),
             new ErpApp.Domain.Accounting.GlLineInput(Guid.NewGuid(), 0m, 10m)],
            null);

        db.GlJournalEntries.Add(entry);
        await db.SaveChangesAsync(CancellationToken.None);

        db.GlJournalEntries.Entry(entry).Property(nameof(ErpApp.Domain.Accounting.GlJournalEntry.PostedAt))
            .CurrentValue = postedAt;
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
