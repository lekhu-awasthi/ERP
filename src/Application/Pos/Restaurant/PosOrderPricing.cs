using ErpApp.Application.Catalog;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Restaurant;

/// <summary>An item a waiter adds: what, how many, in which unit, and a note for the kitchen. No rate.</summary>
public sealed record PosOrderItemInput(Guid ProductId, decimal Quantity, Guid? UnitId, string? Note);

/// <summary>More of a line already on the order.</summary>
public sealed record PosOrderLineQuantityInput(Guid LineId, decimal Quantity);

/// <summary>
/// Phase 64 -- turns what a waiter added into priced, routed order lines, from the catalogue and never
/// from the request (docs/phase-64-status.md Decision C): the rate is the product's VAT-exclusive price
/// in the chosen unit after the tenant's price basis (the grid's own figure), the service charge is
/// <see cref="PosServiceCharge"/>'s, and the kitchen is the product's station.
///
/// <para>A product the till would not sell here -- not available for sale, inactive, a variant parent,
/// or restricted to other locations -- is a 409 naming it, as the sale's is (phase 61 Decision M).</para>
/// </summary>
internal static class PosOrderPricing
{
    public static async Task<IReadOnlyList<PosOrderNewLine>> PriceAsync(
        IAppDbContext db,
        PosTillContext till,
        PosTab orderType,
        IReadOnlyList<PosOrderItemInput> items,
        string field,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var organizationId = till.Location.OrganizationId;
        var locationId = till.Location.Id;
        var ids = items.Select(x => x.ProductId).Distinct().ToList();

        var products = await db.Products
            .AsNoTracking()
            .Include(x => x.SecondaryUnits)
            .Include(x => x.Locations)
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (products.Count != ids.Count)
        {
            throw new ValidationException([new ValidationFailure(field, "An item names a product that does not exist.")]);
        }

        var unsellable = products.Values
            .Where(p => !p.AvailableForSale || !p.IsActive || p.HasVariants
                || (p.Locations.Count > 0 && p.Locations.All(l => l.LocationId != locationId)))
            .Select(p => p.Name)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unsellable.Count > 0)
        {
            throw new ConflictException(
                $"{string.Join(", ", unsellable)} cannot be ordered at '{till.Location.Name}': each must be "
                + "available for sale, active, not a variant parent, and available at this location.");
        }

        var basis = await PosSellableProducts.PriceBasisAsync(db, organizationId, cancellationToken);
        var lines = new List<PosOrderNewLine>();

        foreach (var item in items)
        {
            var product = products[item.ProductId];

            // The primary unit, or one of the product's own secondary units, priced at that unit's price.
            Guid? unitId = product.PrimaryUnitId;
            var factor = UnitConversion.PrimaryFactor;
            var sellingPrice = product.SellingPrice;

            if (item.UnitId is { } chosen && chosen != product.PrimaryUnitId)
            {
                var secondary = product.SecondaryUnits.SingleOrDefault(u => u.UnitId == chosen)
                    ?? throw new ValidationException([new ValidationFailure(
                        field, $"'{product.Name}' is not sold in that unit.")]);

                unitId = secondary.UnitId;
                factor = secondary.ConversionRate;
                sellingPrice = secondary.SellingPrice;
            }

            lines.Add(new PosOrderNewLine(
                product.Id,
                unitId,
                factor,
                item.Quantity,
                ProductPrices.ToExclusiveRate(sellingPrice, product.VatRate, basis),
                product.VatRate,
                PosServiceCharge.RateFor(till.Settings, product.ServiceChargeApplicable, orderType),
                item.Note,
                product.KitchenStationId));
        }

        return lines;
    }
}
