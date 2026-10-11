using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog;

/// <summary>
/// Phase 81 -- the rates a sales line may start at, which is what "an unchanged rate" means to the
/// ERP's price gate (<c>SalesPriceGate</c>) and what the form suggests (<c>SuggestProductRateQuery</c>).
/// One reader for both, so the clerk's own unchanged suggestion is never the thing refused.
///
/// <para><b>"Recent" is the most recent approved Invoice line for this product in this unit</b>,
/// ordered by the invoice's own date and then by code so two invoices dated the same day resolve
/// deterministically. In <i>this unit</i> since phase 81: a line's rate is per unit entered (phase
/// 52), so a bottle's price suggested for a carton line was wrong before it was a gate. Null is the
/// product's primary unit, as on every stored line. Quotations and Sales Orders are excluded:
/// neither is a sale, and a quoted price that was never accepted is the wrong thing to propagate. A
/// Credit Note is excluded for the mirror reason -- it is a return, not a price.</para>
/// </summary>
public static class ProductRateReferences
{
    public static Task<decimal?> RecentRateAsync(
        IAppDbContext db, Guid organizationId, Guid productId, Guid? unitId, CancellationToken cancellationToken) =>
        (from line in db.InvoiceLines
         join invoice in db.Invoices on line.InvoiceId equals invoice.Id
         where invoice.OrganizationId == organizationId
             && invoice.Status == InvoiceStatus.Approved
             && line.ProductId == productId
             && line.UnitId == unitId
         orderby invoice.Date descending, invoice.Code descending
         select (decimal?)line.Rate)
        .FirstOrDefaultAsync(cancellationToken);
}
