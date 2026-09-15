using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy;

/// <summary>What a tenant has consumed of each metered allowance, and the ceiling it is measured
/// against. A ceiling of <c>0</c> means not metered -- see <see cref="TenantSubscription"/>.</summary>
/// <param name="TransactionsUsed">Distinct metered documents posted within the current <b>allowance
/// year</b> -- not the whole term; see <see cref="SubscriptionUsageReader.CurrentAllowanceYear"/>.</param>
/// <param name="ProductsUsed">Products the tenant currently holds, variants included.</param>
/// <param name="AiScansUsed">Phase 46 -- extraction attempts made so far on the current Nepal-local
/// day.</param>
/// <param name="LocationsUsed">Phase 46 -- billing locations the tenant currently holds. Reported
/// beside <paramref name="LocationQuota"/> for display only; nothing refuses on it.</param>
public sealed record SubscriptionUsage(
    int TransactionsUsed,
    int TransactionQuota,
    int ProductsUsed,
    int ProductQuota,
    int AiScansUsed,
    int DailyAiScanQuota,
    int LocationsUsed,
    int LocationQuota)
{
    public static readonly SubscriptionUsage None = new(0, 0, 0, 0, 0, 0, 0, 0);

    public bool TransactionsMetered => TransactionQuota > 0;

    public bool ProductsMetered => ProductQuota > 0;

    public bool AiScansMetered => DailyAiScanQuota > 0;

    /// <summary>Whether a purchased location count has been recorded at all. Never a refusal --
    /// <c>LocationQuota</c> is a record of what was bought, not a ceiling.</summary>
    public bool LocationsRecorded => LocationQuota > 0;

    public int TransactionsRemaining => TransactionsMetered ? Math.Max(TransactionQuota - TransactionsUsed, 0) : 0;

    public int ProductsRemaining => ProductsMetered ? Math.Max(ProductQuota - ProductsUsed, 0) : 0;

    public int AiScansRemaining => AiScansMetered ? Math.Max(DailyAiScanQuota - AiScansUsed, 0) : 0;
}

/// <summary>
/// Phase 41 -- the single place either metered figure is computed.
///
/// <para><b>One reader, two consumers, agreeing by construction.</b> <c>SubscriptionQuotaBehavior</c>
/// blocks on these numbers and the Subscription screen displays them. Phase 26b's rule, and phase
/// 36's correction of it: two call sites that each derive "the same" figure do not agree, they
/// coincide -- and the pair that coincides is the pair that will one day differ, here in the worst
/// possible way, with a screen showing 4,900 of 5,000 while every approve is refused.</para>
///
/// <para><b>Derived, not cached, and the cost is paid only by tenants who bought a ceiling.</b> A
/// stored counter would be one row to read, but it drifts: it has two writers (approve, and any
/// direct data fix), so phase-21a's rule against a concurrency token on a two-writer row applies and
/// nothing would reconcile it. Counting instead means the number is always the truth. The cost is
/// bounded by the thing that makes it safe -- a quota of 0 is not metered, so the count never runs
/// for a trial, and the behavior asks for a figure only when a ceiling exists to compare it to.</para>
///
/// <para><b>Distinct source documents, not GL entries.</b> Phase 36 retired "one GL entry per
/// approved document": a document can carry several (a void posts its reversal as a second entry
/// against the same source, and an edited Opening Balance line posts repeatedly). Counting entries
/// would charge a tenant twice for approving and then voiding one invoice. The Terms' unit is the
/// transaction, so the count is over distinct <c>(SourceDocumentType, SourceDocumentId)</c>.</para>
/// </summary>
public static class SubscriptionUsageReader
{
    /// <summary>
    /// The metered document types, as a materialised array so the <c>Contains</c> below translates
    /// to a SQL <c>IN</c> rather than being funcletized per call.
    /// </summary>
    private static readonly DocumentType[] MeteredTypes = [.. DocumentMechanisms.MeteredTransactions];

    /// <summary>
    /// Phase 46 -- the window the transaction ceiling is actually counted over: the <b>allowance
    /// year</b> the given instant falls in, which is the anniversary year running from
    /// <c>TermStartsAt</c>, clamped to <c>TermEndsAt</c>.
    ///
    /// <para><b>Phase 41 counted over the whole term, and the price list says that is wrong.</b>
    /// Every published quota is stated per year -- "Up to 50,000 transactions / year" -- and the
    /// 3-year tab states the same 50,000 beside a tripled price (confirmed live 2026-09-15: the
    /// quota lines are byte-identical across the 1-year, 3-year and Lifetime tabs, only the price
    /// moves). So a term is a number of allowance years bought at once, not one long allowance: a
    /// tenant on a 3-year term is sold 50,000 per year and would otherwise have received 50,000 for
    /// the three, running out in year one with two years still paid for. A Lifetime term (ten years,
    /// per the same page) makes the same bug ten times worse.</para>
    ///
    /// <para>Anniversaries come from <c>AddYears</c>, so a term starting 29 February lands on the
    /// 28th in common years rather than drifting by a day a year.</para>
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) CurrentAllowanceYear(
        TenantSubscription subscription, DateTimeOffset asOf)
    {
        var start = subscription.TermStartsAt;

        // Walk whole years rather than dividing elapsed days by 365: only AddYears knows what the
        // anniversary of 29 February is. A term is at most ten years (the Lifetime tier), so this is
        // a handful of iterations, and it is bounded by the term end regardless.
        while (start.AddYears(1) <= asOf && start.AddYears(1) < subscription.TermEndsAt)
        {
            start = start.AddYears(1);
        }

        var end = start.AddYears(1);

        return (start, end > subscription.TermEndsAt ? subscription.TermEndsAt : end);
    }

    /// <summary>
    /// Counts the tenant's metered transactions for the current allowance year. Returns 0 without
    /// touching the database when the tenant has no transaction ceiling.
    /// </summary>
    public static async Task<int> CountTransactionsAsync(
        IAppDbContext db, TenantSubscription subscription, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        if (subscription.TransactionQuota <= 0)
        {
            return 0;
        }

        var (from, to) = CurrentAllowanceYear(subscription, asOf);

        return await db.GlJournalEntries
            .AsNoTracking()
            .Where(x => x.OrganizationId == subscription.OrganizationId
                && x.PostedAt >= from
                && x.PostedAt < to
                && MeteredTypes.Contains(x.SourceDocumentType))
            .Select(x => new { x.SourceDocumentType, x.SourceDocumentId })
            .Distinct()
            .CountAsync(cancellationToken);
    }

    /// <summary>
    /// Phase 46 -- counts the AI extraction attempts already made on the Nepal-local day
    /// <paramref name="asOf"/> falls in. Returns 0 without touching the database when the tenant has
    /// no scan ceiling.
    ///
    /// <para><b>It counts audit rows, not the document's own timestamp, and that is the whole
    /// design.</b> <c>UploadedDocument.ExtractionAttemptedAt</c> is overwritten in place by every
    /// re-extraction, so it holds one instant per document and answers "when was this last scanned"
    /// -- not "how many scans happened today". Ten re-runs against one bill would count as one. This
    /// is phase-26c's rule in a new place: a dated report derives from the append-only history, never
    /// from the field that is updated in place.</para>
    ///
    /// <para>The append-only history already exists: phase 22 audits every extraction, because it is
    /// the one action in the product that sends a customer's document outward. <c>AuditBehavior</c>
    /// writes its row <i>after</i> the handler succeeds, so a request being metered right now has not
    /// written one yet and the count is exactly "attempts before this one today" -- which is the
    /// figure the ceiling compares against.</para>
    /// </summary>
    public static async Task<int> CountAiScansTodayAsync(
        IAppDbContext db, TenantSubscription subscription, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        if (subscription.DailyAiScanQuota <= 0)
        {
            return 0;
        }

        var dayStart = NepalTime.StartOfLocalDay(asOf);
        var dayEnd = dayStart.AddDays(1);

        return await db.Audits
            .AsNoTracking()
            .Where(x => x.OrganizationId == subscription.OrganizationId
                && x.DocumentType == DocumentType.DocumentExtraction
                && x.CreatedAt >= dayStart
                && x.CreatedAt < dayEnd)
            .CountAsync(cancellationToken);
    }

    /// <summary>
    /// Phase 46 -- how many billing locations the tenant holds. Unlike the other three this is never
    /// compared against anything in order to refuse: the reference product does not cap locations
    /// (confirmed live -- see <see cref="TenantSubscription.LocationQuota"/>), so this figure exists
    /// so the subscription screen can show what is in use beside what was purchased.
    /// </summary>
    public static async Task<int> CountLocationsAsync(
        IAppDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        return await db.BillingLocations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .CountAsync(cancellationToken);
    }

    /// <summary>
    /// Counts the tenant's products. Returns 0 without touching the database when the tenant has no
    /// product ceiling.
    /// </summary>
    public static async Task<int> CountProductsAsync(
        IAppDbContext db, TenantSubscription subscription, CancellationToken cancellationToken)
    {
        if (subscription.ProductQuota <= 0)
        {
            return 0;
        }

        return await db.Products
            .AsNoTracking()
            .Where(x => x.OrganizationId == subscription.OrganizationId)
            .CountAsync(cancellationToken);
    }

    /// <summary>
    /// Both figures at once, for the Subscription screen. Each half still costs nothing when its own
    /// ceiling is absent, so a tenant metered on one axis pays for one count.
    /// </summary>
    public static async Task<SubscriptionUsage> ReadAsync(
        IAppDbContext db, TenantSubscription subscription, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        return new SubscriptionUsage(
            await CountTransactionsAsync(db, subscription, asOf, cancellationToken),
            subscription.TransactionQuota,
            await CountProductsAsync(db, subscription, cancellationToken),
            subscription.ProductQuota,
            await CountAiScansTodayAsync(db, subscription, asOf, cancellationToken),
            subscription.DailyAiScanQuota,
            await CountLocationsAsync(db, subscription.OrganizationId, cancellationToken),
            subscription.LocationQuota);
    }
}
