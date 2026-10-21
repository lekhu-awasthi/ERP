using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 95 -- the company's two tracking switches (Configurations &gt; General, as the vendor's are: read live in
/// phases 91 and 93). The user's rule (2026-10-20): a switch that is off overrides every product's own, so the product
/// is not tracked; a switch that is on lets each product's own decide. A product keeps its own flag either way, so
/// switching the company back on restores what each product had.
///
/// <para><b>Ask this, never the product.</b> Every rule that decides whether a line needs, may carry or must not carry
/// a batch or serial asks <see cref="TracksBatches"/>/<see cref="TracksSerials"/>. A reader that asked
/// <c>Product.BatchTracking</c> alone would demand a batch the company has switched off.</para>
/// </summary>
public sealed record StockTrackingSwitches(bool Batch, bool Serial)
{
    /// <summary>Both on: each product's own flag decides, as before phase 95.</summary>
    public static readonly StockTrackingSwitches ProductsDecide = new(true, true);

    public bool TracksBatches(Product product) => Batch && product.BatchTracking;

    public bool TracksSerials(Product product) => Serial && product.SerialTracking;

    /// <summary>The company's switches; with no settings row (every company created through the wizard has one), each
    /// product decides, as it did before the switches existed.</summary>
    public static async Task<StockTrackingSwitches> LoadAsync(
        IAppDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
        await db.TenantSettings
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new StockTrackingSwitches(x.BatchTrackingEnabled, x.SerialTrackingEnabled))
            .SingleOrDefaultAsync(cancellationToken)
        ?? ProductsDecide;
}
