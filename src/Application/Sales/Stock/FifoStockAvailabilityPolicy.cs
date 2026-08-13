using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Stock;

/// <summary>
/// The real Phase 7 implementation, replacing Phase 5's AlwaysOkStockAvailabilityPolicy stub.
/// Only Invoice.Type==Goods lines touch stock (a Service line is skipped entirely -- there's
/// nothing to check). For each remaining line, sums requested Quantity per ProductId (a line-split
/// product could appear more than once) and compares against
/// IStockLedgerService.GetAvailableQuantityAsync for (ProductId, Invoice.WarehouseId). The first
/// product with a shortfall decides the whole invoice's status -- once any shortfall exists, this
/// stops checking further lines and asks TenantSettings.NegativeStockBalanceAction what to do.
/// </summary>
public sealed class FifoStockAvailabilityPolicy(IAppDbContext db, IStockLedgerService stockLedgerService)
    : IStockAvailabilityPolicy
{
    public async Task<StockAvailabilityStatus> CheckAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var productIds = invoice.Lines.Select(x => x.ProductId).Distinct().ToList();

        var productTypes = await db.Products
            .Where(x => x.OrganizationId == invoice.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Type })
            .ToDictionaryAsync(x => x.Id, x => x.Type, cancellationToken);

        var requestedByProduct = invoice.Lines
            .Where(x => productTypes.TryGetValue(x.ProductId, out var type) && type == ProductType.Goods)
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var hasShortfall = false;
        foreach (var (productId, requestedQuantity) in requestedByProduct)
        {
            var available = await stockLedgerService.GetAvailableQuantityAsync(
                invoice.OrganizationId, productId, invoice.WarehouseId, cancellationToken);

            if (requestedQuantity > available)
            {
                hasShortfall = true;
                break;
            }
        }

        if (!hasShortfall)
        {
            return StockAvailabilityStatus.Ok;
        }

        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == invoice.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        return settings.NegativeStockBalanceAction switch
        {
            BalanceAction.Reject => StockAvailabilityStatus.Reject,
            BalanceAction.DoNothing => StockAvailabilityStatus.Ok,
            _ => StockAvailabilityStatus.Warn,
        };
    }
}
