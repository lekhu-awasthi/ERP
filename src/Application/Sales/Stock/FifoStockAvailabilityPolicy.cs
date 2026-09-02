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
/// product with a shortfall decides the whole document's status -- once any shortfall exists, this
/// stops checking further lines and asks TenantSettings.NegativeStockBalanceAction what to do.
///
/// <para>Phase 25 split the Invoice-specific part (which lines count, and where the warehouse
/// comes from) from the part that is the same for every stock-consuming document, so a Production
/// Journal reaches the identical branch on that setting.</para>
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

        var requirements = invoice.Lines
            .Where(x => productTypes.TryGetValue(x.ProductId, out var type) && type == ProductType.Goods)
            .GroupBy(x => x.ProductId)
            .Select(g => new StockRequirement(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        return await CheckRequirementsAsync(
            invoice.OrganizationId, invoice.WarehouseId, requirements, cancellationToken);
    }

    public async Task<StockAvailabilityStatus> CheckRequirementsAsync(
        Guid organizationId,
        Guid warehouseId,
        IReadOnlyCollection<StockRequirement> requirements,
        CancellationToken cancellationToken)
    {
        var hasShortfall = false;
        foreach (var requirement in requirements)
        {
            var available = await stockLedgerService.GetAvailableQuantityAsync(
                organizationId, requirement.ProductId, warehouseId, cancellationToken);

            if (requirement.Quantity > available)
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
            x => x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        return settings.NegativeStockBalanceAction switch
        {
            BalanceAction.Reject => StockAvailabilityStatus.Reject,
            BalanceAction.DoNothing => StockAvailabilityStatus.Ok,
            _ => StockAvailabilityStatus.Warn,
        };
    }
}
