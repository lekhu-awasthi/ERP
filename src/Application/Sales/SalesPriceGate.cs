using ErpApp.Application.Catalog;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Sales.Discounts;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales;

/// <summary>
/// Phase 81 -- who may price an ERP sale. A Quotation, Sales Order or Invoice draft that discounts
/// needs <see cref="PermissionKeys.InvoiceDiscount"/>, and one whose rate is not one the form would
/// suggest needs <see cref="PermissionKeys.InvoiceChangeRate"/>, each at the document's location
/// (organization-wide when the type is outside the tenant's location scope). The till's own keys
/// (phase 76) are a separate question about a separate channel.
///
/// <para><b>Only what the request changes is judged.</b> A line's discount is carried when the draft
/// already held a line of the same product and unit at that discount, and its rate when it held one at
/// that rate -- two questions, so a clerk who takes a manager's discount off a line the manager also
/// repriced is not asked for the rate key. The document's discount is carried when it is the one
/// already saved. Each carried line vouches for one request line per question, so two discounted lines
/// cannot both ride on one. On a conversion the source Quotation is what was carried, and
/// that is safe only because a Quotation passes this same gate: an ungated source would be a way
/// round it. A clerk can therefore edit the quantity on a draft a manager priced, or convert a
/// quotation the manager discounted, without being able to change either price.</para>
///
/// <para><b>Phase 97 -- a discount scheme's own percent is carried too.</b> A line discounted at exactly the percent the
/// schemes give its product here on the document's day, and a document discounted at exactly the slab percent its amount
/// reaches, need no key: the scheme is the business's own price, set by whoever may manage schemes, and the form starts
/// the line there (<see cref="DiscountSchemeRates"/>, one reader for both). Changing it, either way but to none, is an
/// ordinary discount and needs the key (the user, 2026-10-21: "the scheme is the start").</para>
///
/// <para><b>Charging more by removing a discount needs no key</b>: anyone who may draft may charge the
/// full price. A rate is gated in both directions, as at the till (phase 74 Decision C): a raised rate
/// overcharges as invisibly as a cut undercharges. Discount first, then rate, as at the till.</para>
/// </summary>
public static class SalesPriceGate
{
    /// <param name="UnitId">As stored on a line: null is the product's primary unit.</param>
    /// <param name="BatchId">Phase 95 -- the batch an invoice line issues from: its own selling price is a rate the
    /// line keeps, beside the catalogue's.</param>
    public sealed record PricedLine(Guid ProductId, Guid? UnitId, decimal Rate, decimal DiscountPct, Guid? BatchId = null);

    /// <summary>Phase 97 -- what the discount schemes are judged on: the document's day, and its amount before VAT and its
    /// own discount (after each line's) in the base currency, which is what a slab names.</summary>
    public sealed record SchemeBasis(DateOnly Date, decimal BillAmount)
    {
        public static SchemeBasis For(
            DateOnly date, decimal exchangeRate, IEnumerable<(decimal Quantity, decimal Rate, decimal DiscountPct)> lines) =>
            new(date, ExchangeRates.ToBase(lines.Sum(x => x.Quantity * x.Rate * (1 - x.DiscountPct / 100m)), exchangeRate));
    }

    /// <param name="IsBaseCurrency">A document in another currency has no catalogue price to compare a
    /// rate with (the catalogue is in the base currency), so only its discount is judged.</param>
    /// <param name="CarriedDiscountPct">The document discount already saved (or the source's on a
    /// conversion); null on a fresh document.</param>
    /// <param name="CarriedLines">The lines already saved (or the source's); empty on a fresh document.</param>
    public static async Task EnsureMayPriceAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid userId,
        Guid? locationId,
        bool isBaseCurrency,
        decimal discountPct,
        IReadOnlyList<PricedLine> lines,
        decimal? carriedDiscountPct,
        IReadOnlyList<PricedLine> carriedLines,
        SchemeBasis scheme,
        CancellationToken cancellationToken)
    {
        var schemes = await DiscountSchemeRates.LoadAsync(
            db, organizationId, locationId, scheme.Date, [.. lines.Select(x => x.ProductId)], cancellationToken);

        var discounted = Uncarried(
            [.. lines.Where(x => x.DiscountPct > 0m && x.DiscountPct != schemes.ForProduct(x.ProductId))],
            carriedLines, x => (x.ProductId, x.UnitId, x.DiscountPct));

        if ((discountPct > 0m && discountPct != carriedDiscountPct && discountPct != schemes.ForBillAmount(scheme.BillAmount))
            || discounted.Count > 0)
        {
            await EnsureAsync(db, organizationId, userId, PermissionKeys.InvoiceDiscount, locationId, cancellationToken);
        }

        var repriced = Uncarried(lines, carriedLines, x => (x.ProductId, x.UnitId, x.Rate));

        if (isBaseCurrency && await AnyRateChangedAsync(db, organizationId, repriced, cancellationToken))
        {
            await EnsureAsync(db, organizationId, userId, PermissionKeys.InvoiceChangeRate, locationId, cancellationToken);
        }
    }

    /// <summary>What a screen asks before offering the discount and rate inputs; the same
    /// location rule <see cref="EnsureMayPriceAsync"/> refuses on.</summary>
    public static async Task<bool> IsGrantedAsync(
        IAppDbContext db, Guid organizationId, Guid userId, string permissionKey, Guid? locationId,
        CancellationToken cancellationToken) =>
        locationId is { } id
            ? await GrantedPermissionReader.IsGrantedAtLocationAsync(
                db, organizationId, userId, permissionKey, id, cancellationToken)
            : (await GrantedPermissionReader.GrantedKeysAsync(db, organizationId, userId, cancellationToken))
                .Contains(permissionKey);

    /// <summary>The lines no unused carried line matches on <paramref name="key"/>; each carried line is
    /// used at most once.</summary>
    private static List<PricedLine> Uncarried<TKey>(
        IReadOnlyList<PricedLine> lines, IReadOnlyList<PricedLine> carriedLines, Func<PricedLine, TKey> key)
    {
        var unused = carriedLines.Select(key).ToList();
        var changed = new List<PricedLine>();

        foreach (var line in lines)
        {
            var index = unused.IndexOf(key(line));
            if (index >= 0)
            {
                unused.RemoveAt(index);
            }
            else
            {
                changed.Add(line);
            }
        }

        return changed;
    }

    /// <summary>A rate is kept when it is the catalogue's price in the line's unit or, in Recent mode,
    /// the last sale's rate in that unit -- exactly what <c>SuggestProductRateQuery</c> offers. A
    /// product whose catalogue price is zero has none to keep, so whatever is typed is its price.</summary>
    private static async Task<bool> AnyRateChangedAsync(
        IAppDbContext db, Guid organizationId, IReadOnlyList<PricedLine> changed, CancellationToken cancellationToken)
    {
        if (changed.Count == 0)
        {
            return false;
        }

        var productIds = changed.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products
            .AsNoTracking()
            .Include(x => x.SecondaryUnits)
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        // No settings row reads as the defaults (exclusive of VAT, the catalogue's price), as everywhere else.
        var settings = await db.TenantSettings
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { x.ProductPriceBasis, x.SuggestSellingPriceMode })
            .SingleOrDefaultAsync(cancellationToken);
        var basis = settings?.ProductPriceBasis ?? Domain.Tenancy.ProductPriceBasis.ExclusiveOfVat;
        var recentMode = settings?.SuggestSellingPriceMode == Domain.Tenancy.SuggestSellingPriceMode.RecentSellingPrice;

        // Phase 95 -- a batch's own selling price, which picking it prices the line at (ProductPrices.BatchRate).
        var batchIds = changed.Where(x => x.BatchId is not null).Select(x => x.BatchId!.Value).Distinct().ToList();
        var batchPrices = batchIds.Count == 0
            ? []
            : await db.ProductBatches
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && batchIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.SellingPrice, cancellationToken);

        foreach (var line in changed)
        {
            if (!products.TryGetValue(line.ProductId, out var product)
                || ProductPrices.CatalogueRate(product, line.UnitId, basis) is not { } catalogueRate
                || catalogueRate == 0m
                || line.Rate == catalogueRate
                || (line.BatchId is { } batchId
                    && ProductPrices.BatchRate(product, line.UnitId, basis, batchPrices.GetValueOrDefault(batchId)) == line.Rate))
            {
                continue;
            }

            if (recentMode
                && await ProductRateReferences.RecentRateAsync(
                    db, organizationId, line.ProductId, line.UnitId, cancellationToken) == line.Rate)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static Task EnsureAsync(
        IAppDbContext db, Guid organizationId, Guid userId, string permissionKey, Guid? locationId,
        CancellationToken cancellationToken) =>
        locationId is { } id
            ? GrantedPermissionReader.EnsureGrantedAtLocationAsync(
                db, organizationId, userId, permissionKey, id, cancellationToken)
            : GrantedPermissionReader.EnsureGrantedAsync(db, organizationId, userId, permissionKey, cancellationToken);

    /// <summary>The currency question every caller asks of its aggregate after <c>SetCurrency</c>.</summary>
    public static bool IsBase(string currencyCode) => CurrencyCatalog.IsBase(currencyCode);
}
