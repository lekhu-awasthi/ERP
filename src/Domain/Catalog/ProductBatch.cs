namespace ErpApp.Domain.Catalog;

/// <summary>
/// Phase 51 -- a batch's <b>identity</b>, and nothing else. Batch number, manufacture date, expiry
/// date, per product, per tenant.
///
/// <para><b>There is deliberately no Quantity column here, and that is the phase's central
/// decision.</b> Every quantity a batch has is derived from the FIFO layers that carry its
/// <see cref="Id"/>: on-hand per warehouse is <c>SUM(QuantityRemaining)</c> over
/// <see cref="Inventory.StockLedgerEntry"/> filtered to this batch, and dated movement is
/// <see cref="Inventory.StockMovement"/> filtered the same way. The alternative -- a batch-quantity
/// ledger reconciled against the layers -- creates a second quantity that can disagree with the
/// first, which is exactly the failure mode phase 37 describes as "any two of three views can be
/// patched into agreement while the third drifts". A dimension that is a <c>GROUP BY</c> cannot
/// drift from the thing it groups.</para>
///
/// <para><b>Both dates are nullable, on purpose.</b> The 2026-09-16 read observed one row carrying
/// both, which is one sample, and a lot number with no expiry is ordinary in the trades this
/// product serves. A consequence worth naming rather than discovering: <c>TenantIndexConvention</c>
/// inspects only <i>required</i> <c>DateOnly</c> properties, so this entity classifies as master
/// data -- and it does so <i>by rule</i>, because <c>(OrganizationId, ProductId, BatchNo)</c> is a
/// per-tenant unique index leading with <c>OrganizationId</c> exactly like every other master-data
/// table. <c>Infrastructure.UnitTests</c> asserts both halves, so a later phase making either date
/// required is told by a failing test that it now owes the convention a declaration (phase 50's
/// lesson about being right by luck).</para>
///
/// <para>A batch is minted on <b>receipt</b> -- a Purchase Bill (or Opening Stock, or an Inventory
/// Adjustment increase, or a Production Journal output) line naming a batch number that does not
/// exist yet creates one -- and is reused by every later receipt of the same number. There is no
/// batch-management screen and no permission key of its own: reading one rides
/// <c>Catalog.Product.View</c> and creating one rides the approving document's key, the same
/// reasoning phase 24 used for variants.</para>
/// </summary>
public sealed class ProductBatch
{
    public const int BatchNoMaxLength = 100;

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ProductId { get; private set; }

    /// <summary>Unique per (OrganizationId, ProductId). Trimmed; never empty.</summary>
    public string BatchNo { get; private set; } = null!;

    public DateOnly? ManufactureDate { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ProductBatch()
    {
    }

    public static ProductBatch Create(
        Guid organizationId,
        Guid productId,
        string batchNo,
        DateOnly? manufactureDate,
        DateOnly? expiryDate)
    {
        var trimmed = (batchNo ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("A batch needs a batch number.");
        }

        if (trimmed.Length > BatchNoMaxLength)
        {
            throw new InvalidOperationException(
                $"A batch number cannot be longer than {BatchNoMaxLength} characters.");
        }

        EnsureDatesOrdered(manufactureDate, expiryDate);

        return new ProductBatch
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProductId = productId,
            BatchNo = trimmed,
            ManufactureDate = manufactureDate,
            ExpiryDate = expiryDate,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// A later receipt of the same batch number may carry dates the first one did not -- the goods
    /// arrive, the paperwork follows. Filling a blank is allowed; <b>changing</b> a date that is
    /// already set is not, because the batch is already attached to layers that were costed and
    /// reported under it, and a silently moving expiry date is the kind of thing that reads as a
    /// data-entry fix right up until a report disagrees with a printed document.
    /// </summary>
    public void FillMissingDates(DateOnly? manufactureDate, DateOnly? expiryDate)
    {
        var newManufacture = ManufactureDate ?? manufactureDate;
        var newExpiry = ExpiryDate ?? expiryDate;

        EnsureDatesOrdered(newManufacture, newExpiry);

        ManufactureDate = newManufacture;
        ExpiryDate = newExpiry;
    }

    /// <summary>The batch number is the identity, so this is a rename of the same physical batch --
    /// used only by the edit path on the product's Batch tab. Uniqueness is the database's job.</summary>
    public void Update(string batchNo, DateOnly? manufactureDate, DateOnly? expiryDate)
    {
        var trimmed = (batchNo ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("A batch needs a batch number.");
        }

        if (trimmed.Length > BatchNoMaxLength)
        {
            throw new InvalidOperationException(
                $"A batch number cannot be longer than {BatchNoMaxLength} characters.");
        }

        EnsureDatesOrdered(manufactureDate, expiryDate);

        BatchNo = trimmed;
        ManufactureDate = manufactureDate;
        ExpiryDate = expiryDate;
    }

    private static void EnsureDatesOrdered(DateOnly? manufactureDate, DateOnly? expiryDate)
    {
        if (manufactureDate is { } made && expiryDate is { } expires && expires < made)
        {
            throw new InvalidOperationException("A batch cannot expire before it was manufactured.");
        }
    }
}
