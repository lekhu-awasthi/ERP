using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Posting;

/// <summary>
/// Phase 87 -- the accounts a sale recorded at its approval, read for whatever reverses it (a credit note returning it or
/// adjusting its price). A line's Sales account is found by product: one approval resolves one account per product, so
/// every line of a product on one invoice recorded the same one. A null (a sale from before phase 87, a leg the sale did
/// not post, a product the sale never had) leaves the resolver to today's settings.
/// </summary>
internal sealed record RecordedSaleAccounts(
    Guid? ReceivableAccountId,
    Guid? VatPayableAccountId,
    Guid? CogsAccountId,
    Guid? InventoryAccountId,
    Guid? ServiceChargeAccountId,
    Guid? PackagingFeeAccountId,
    Guid? LocationId,
    IReadOnlyDictionary<Guid, Guid> SalesAccountByProduct)
{
    public Guid? SalesAccountFor(Guid productId) =>
        SalesAccountByProduct.TryGetValue(productId, out var accountId) ? accountId : null;

    public static RecordedSaleAccounts Of(Invoice invoice) => new(
        invoice.ReceivableAccountId,
        invoice.VatPayableAccountId,
        invoice.CogsAccountId,
        invoice.InventoryAccountId,
        invoice.ServiceChargeAccountId,
        invoice.PackagingFeeAccountId,
        invoice.LocationId,
        invoice.Lines
            .Where(x => x.SalesAccountId is not null)
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.First().SalesAccountId!.Value));

    /// <summary>The invoice <paramref name="invoiceId"/> with its lines, or null when there is none (a standalone note, or
    /// one naming an invoice from before the system).</summary>
    public static async Task<RecordedSaleAccounts?> LoadAsync(
        IAppDbContext db, Guid organizationId, Guid? invoiceId, CancellationToken cancellationToken)
    {
        if (invoiceId is not { } id)
        {
            return null;
        }

        var invoice = await db.Invoices
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);
        return invoice is null ? null : Of(invoice);
    }
}
