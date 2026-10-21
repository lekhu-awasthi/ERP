using ErpApp.Application.Catalog;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Sales.Discounts;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Restaurant;

/// <summary>An item a waiter adds: what, how many, in which unit, and a note for the kitchen. No rate. Phase 95 --
/// and the batch it is served from, for a batch-tracked product (a 400 naming the item without one).</summary>
public sealed record PosOrderItemInput(Guid ProductId, decimal Quantity, Guid? UnitId, string? Note, string? BatchNo = null);

/// <summary>More of a line already on the order.</summary>
public sealed record PosOrderLineQuantityInput(Guid LineId, decimal Quantity);

/// <summary>
/// Phase 64 -- turns what a waiter added into priced, routed order lines, from the catalogue and never
/// from the request (docs/phase-64-status.md Decision C): the rate is the product's VAT-exclusive price
/// in the chosen unit after the tenant's price basis (the grid's own figure), the service charge is
/// <see cref="PosServiceCharge"/>'s, the packaging fee <see cref="PosPackagingFee"/>'s (phase 70: a Take
/// Away or Delivery order's lines are parcels), and the kitchen is the product's station.
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

        // Phase 91 Decision C -- a kitchen ticket, a split and a transfer have nowhere to name a unit's serial, so a
        // serial-tracked product sells at a Retail till, where the cart names each one.
        // Phase 95 -- as the company's switch lets it be: off overrides the product's own.
        var switches = await StockTrackingSwitches.LoadAsync(db, organizationId, cancellationToken);
        var serialised = products.Values
            .Where(p => switches.TracksSerials(p))
            .Select(p => p.Name)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (serialised.Count > 0)
        {
            throw new ConflictException(
                $"{string.Join(", ", serialised)} {(serialised.Count == 1 ? "is" : "are")} serial-tracked, and an order "
                + "cannot name which unit a guest was given. Sell it at a Retail till, which names each serial.");
        }

        // Phase 95 -- a batch-tracked item names the batch it is served from, as the vendor's order screen requires
        // and the Retail till's line does (phase 94): resolved as an issue's, so a blank or unknown batch is a 400.
        IReadOnlyList<Guid?> batches;
        try
        {
            batches = await DocumentLineAllocationWriter.ResolveBatchesAsync(
                db, organizationId,
                [.. items.Select(x => new DocumentLineAllocationWriter.LineAllocationInput(
                    x.ProductId, x.Quantity, x.BatchNo, null, null, null))],
                StockLineKind.Issue, cancellationToken);
        }
        catch (ValidationException ex)
        {
            // The writer names a document's lines; an order's request names its items.
            throw new ValidationException(ex.Errors.Select(e => new ValidationFailure(
                e.PropertyName.StartsWith("Lines", StringComparison.Ordinal) ? field + e.PropertyName[5..] : e.PropertyName,
                e.ErrorMessage)));
        }

        var basis = await PosSellableProducts.PriceBasisAsync(db, organizationId, cancellationToken);
        var lines = new List<PosOrderNewLine>();

        // Phase 97 -- each dish starts at its discount scheme's percent, on the day it is ordered (PosOrderSchemes).
        var schemes = await DiscountSchemeRates.LoadAsync(
            db, organizationId, locationId, PosOrderSchemes.Today, ids, cancellationToken);

        // Phase 95 -- an item served from a batch with its own price is priced at it, as picking it on the vendor's
        // order screen does; the rate is frozen on the order line like any other.
        var batchIds = batches.Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var batchPrices = batchIds.Count == 0
            ? []
            : await db.ProductBatches
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && batchIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.SellingPrice, cancellationToken);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var product = products[item.ProductId];

            // The primary unit, or one of the product's own secondary units, priced at that unit's price
            // (ProductPrices.CatalogueRate, phase 72: the figure a retail line below needs Approve for).
            Guid? unitId = product.PrimaryUnitId;
            var factor = UnitConversion.PrimaryFactor;

            if (item.UnitId is { } chosen && chosen != product.PrimaryUnitId)
            {
                var secondary = product.SecondaryUnits.SingleOrDefault(u => u.UnitId == chosen)
                    ?? throw new ValidationException([new ValidationFailure(
                        field, $"'{product.Name}' is not sold in that unit.")]);

                unitId = secondary.UnitId;
                factor = secondary.ConversionRate;
            }

            lines.Add(new PosOrderNewLine(
                product.Id,
                unitId,
                factor,
                item.Quantity,
                (batches[i] is { } batchId
                    ? ProductPrices.BatchRate(product, unitId, basis, batchPrices.GetValueOrDefault(batchId))
                    : null)
                    ?? ProductPrices.CatalogueRate(product, unitId, basis)!.Value,
                product.VatRate,
                PosServiceCharge.RateFor(till.Settings, product.ServiceChargeApplicable, orderType),
                PosPackagingFee.For(till.Settings, product.PackagingFeeApplicable, orderType),
                item.Note,
                product.KitchenStationId,
                batches[i],
                schemes.ForProduct(product.Id)));
        }

        return lines;
    }
}
