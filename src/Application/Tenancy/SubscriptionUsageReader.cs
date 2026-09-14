using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy;

/// <summary>What a tenant has consumed of each metered allowance, and the ceiling it is measured
/// against. A ceiling of <c>0</c> means not metered -- see <see cref="TenantSubscription"/>.</summary>
/// <param name="TransactionsUsed">Distinct metered documents posted within the current term.</param>
/// <param name="ProductsUsed">Products the tenant currently holds, variants included.</param>
public sealed record SubscriptionUsage(
    int TransactionsUsed,
    int TransactionQuota,
    int ProductsUsed,
    int ProductQuota)
{
    public static readonly SubscriptionUsage None = new(0, 0, 0, 0);

    public bool TransactionsMetered => TransactionQuota > 0;

    public bool ProductsMetered => ProductQuota > 0;

    public int TransactionsRemaining => TransactionsMetered ? Math.Max(TransactionQuota - TransactionsUsed, 0) : 0;

    public int ProductsRemaining => ProductsMetered ? Math.Max(ProductQuota - ProductsUsed, 0) : 0;
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
    /// Counts the tenant's metered transactions for the current term. Returns 0 without touching the
    /// database when the tenant has no transaction ceiling.
    /// </summary>
    public static async Task<int> CountTransactionsAsync(
        IAppDbContext db, TenantSubscription subscription, CancellationToken cancellationToken)
    {
        if (subscription.TransactionQuota <= 0)
        {
            return 0;
        }

        return await db.GlJournalEntries
            .AsNoTracking()
            .Where(x => x.OrganizationId == subscription.OrganizationId
                && x.PostedAt >= subscription.TermStartsAt
                && x.PostedAt < subscription.TermEndsAt
                && MeteredTypes.Contains(x.SourceDocumentType))
            .Select(x => new { x.SourceDocumentType, x.SourceDocumentId })
            .Distinct()
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
        IAppDbContext db, TenantSubscription subscription, CancellationToken cancellationToken)
    {
        return new SubscriptionUsage(
            await CountTransactionsAsync(db, subscription, cancellationToken),
            subscription.TransactionQuota,
            await CountProductsAsync(db, subscription, cancellationToken),
            subscription.ProductQuota);
    }
}
