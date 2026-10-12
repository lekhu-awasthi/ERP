using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales;

/// <summary>
/// Phase 86 -- the warehouse each credit note's goods went back into, for the list and the detail page alike, so the two
/// cannot disagree (phase 36's shared-reader rule). Named only where stock actually moved, which is exactly when
/// <c>CreditNoteApprovalPosting</c> restocks: the note is <b>Approved</b> (a draft has moved nothing; a void has taken it
/// back out), it returns an <b>invoice</b> (a standalone note or a price adjustment moves no stock), and it carries a
/// <b>Goods</b> line (a refund of services moves none). The warehouse is the note's own (a till refund since phase 84,
/// another branch's when refunded there), else its invoice's, where every earlier return went.
/// </summary>
internal static class CreditNoteReturnWarehouses
{
    public static async Task<IReadOnlyDictionary<Guid, string>> LoadAsync(
        IAppDbContext db, Guid organizationId, IReadOnlyCollection<CreditNote> notes, CancellationToken cancellationToken)
    {
        var candidates = notes
            .Where(x => x.Status == CreditNoteStatus.Approved && x.ReferrerType == DocumentType.Invoice && x.ReferrerId != null)
            .ToList();
        if (candidates.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // A page's worth of ids at most (MAX_PAGE_SIZE), so the OPENJSON parameter stays as short as the page.
        var ids = candidates.Select(x => x.Id).ToList();
        var withGoods = (await (
                from line in db.CreditNoteLines
                join product in db.Products on line.ProductId equals product.Id
                where ids.Contains(line.CreditNoteId) && product.OrganizationId == organizationId
                    && product.Type == ProductType.Goods
                select line.CreditNoteId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var returned = candidates.Where(x => withGoods.Contains(x.Id)).ToList();
        var invoiceIds = returned.Where(x => x.WarehouseId is null).Select(x => x.ReferrerId!.Value).Distinct().ToList();
        var invoiceWarehouses = invoiceIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await db.Invoices
                .Where(x => x.OrganizationId == organizationId && invoiceIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.WarehouseId, cancellationToken);

        var warehouseOf = returned
            .Select(x => (x.Id, Warehouse: x.WarehouseId ?? (invoiceWarehouses.TryGetValue(x.ReferrerId!.Value, out var w) ? w : (Guid?)null)))
            .Where(x => x.Warehouse is not null)
            .ToDictionary(x => x.Id, x => x.Warehouse!.Value);

        var warehouseIds = warehouseOf.Values.Distinct().ToList();
        var names = await db.Warehouses
            .Where(x => x.OrganizationId == organizationId && warehouseIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return warehouseOf
            .Where(x => names.ContainsKey(x.Value))
            .ToDictionary(x => x.Key, x => names[x.Value]);
    }
}
