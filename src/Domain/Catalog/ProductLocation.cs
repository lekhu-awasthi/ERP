namespace ErpApp.Domain.Catalog;

/// <summary>
/// Phase 36 -- one billing location a Product is available at.
///
/// <para><b>The set is a restriction, and an empty set means "everywhere".</b> That is the live
/// control's own shape: the New Product form's Location field is a checkbox multi-select rendering
/// <c>All</c> when nothing is ticked and carrying no required marker (confirmed 2026-09-07, and
/// again on 2026-09-11 while proving what it does). Storing "all locations" as one row per location
/// would be a different thing -- it would go stale the moment a tenant adds a location, silently
/// hiding the product there.</para>
///
/// <para><b>What it restricts was proved by experiment</b>, because no screen reveals it: the
/// Products grid has no LOCATION column and no location filter. With a product scoped to POS Retail
/// alone, the Invoice form's line picker returned <i>no data</i> while the document's header
/// location was HeadOffice and returned that product as soon as the header was switched to POS
/// Retail -- one <c>products-minimized?…&amp;location_id=…</c> call per switch, so the filtering is
/// the server's, not the browser's. See docs/phase-36-status.md.</para>
///
/// <para>Child entity of Product with no behaviour of its own, created only via
/// <see cref="Product.SetLocations"/> -- the same shape as <see cref="ProductSecondaryUnit"/>.</para>
/// </summary>
public sealed class ProductLocation
{
    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid LocationId { get; private set; }

    private ProductLocation()
    {
    }

    internal static ProductLocation Create(Guid productId, Guid locationId)
    {
        return new ProductLocation
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            LocationId = locationId,
        };
    }
}
