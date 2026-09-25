using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.Sales;

/// <summary>
/// Phase 58 -- a Delivery Note: goods leaving one warehouse for a customer, recorded apart from the
/// Invoice that bills them. The sales side of the <b>physical</b> stock ledger (see
/// <c>Domain.Inventory.StockBooks</c>), and the mirror of <c>Purchasing.GoodsReceivedNote</c>.
///
/// <para><b>What Approve does.</b> It checks the physical ledger for enough stock (through the
/// tenant's own Negative Item Balance setting -- the reference product refused a DN for 8 against a
/// physical balance of 7, with Reject on, while the accounting balance was 3), draws the number, and
/// writes one <c>PhysicalStockMovement</c> Out row per Goods line. No FIFO layer is consumed, no COGS
/// is computed, nothing is posted: the Invoice does all of that, in both modes.</para>
///
/// <para><b>Shape.</b> The <see cref="SalesOrder"/> header plus a warehouse, the live form's
/// <see cref="ExpectedDeliveryDate"/> (required there, defaulting to the day after), a
/// <see cref="ShippingAddress"/> and a tracking number, with Terms and Conditions because the live
/// form carries that block -- a Delivery Note is something this organization issues to its customer,
/// which is phase 27b's own dividing line for Terms.</para>
/// </summary>
public sealed class DeliveryNote
{
    public const string DraftCode = "DRAFT";

    private readonly List<DeliveryNoteLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }

    /// <summary>When the customer should expect the goods. Required on the live form, where it
    /// defaults to the note's date plus one day; no rule relates it to <see cref="Date"/>.</summary>
    public DateOnly ExpectedDeliveryDate { get; private set; }

    public string? Reference { get; private set; }

    /// <summary>The consignment number, as on <c>GoodsReceivedNote.TrackingNo</c>.</summary>
    public string? TrackingNo { get; private set; }

    /// <summary>Where the goods are going, free text -- the live form's "Shipping Address", which is
    /// not pre-filled from the customer's own address.</summary>
    public string? ShippingAddress { get; private set; }

    public DeliveryNoteStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>Phase 28's currency pair -- carried, never converted into a ledger. See
    /// <c>GoodsReceivedNote.CurrencyCode</c>.</summary>
    public string CurrencyCode { get; private set; } = CurrencyCatalog.BaseCode;

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal ExchangeRate { get; private set; } = ExchangeRates.BaseRate;

    /// <summary>Phase 32's billing location -- see <c>SalesOrder.LocationId</c>.</summary>
    public Guid? LocationId { get; private set; }

    public decimal DiscountPct { get; private set; }

    /// <summary>Phase 20b's list-grid Stage picker -- the live "Delivery Note Order Status" group
    /// (Pending / Dispatched / Delivered).</summary>
    public Guid? CustomStatusId { get; private set; }

    /// <summary>Phase 27b's Terms and Conditions text -- see <c>SalesOrder.Terms</c>.</summary>
    public string? Terms { get; private set; }

    public DocumentType? ReferrerType { get; private set; }
    public Guid? ReferrerId { get; private set; }

    public IReadOnlyList<DeliveryNoteLine> Lines => _lines;

    private DeliveryNote()
    {
    }

    public static DeliveryNote Create(
        Guid organizationId, Guid contactId, Guid warehouseId, DateOnly date, DateOnly expectedDeliveryDate,
        string? reference, string? trackingNo, string? shippingAddress,
        DocumentType? referrerType, Guid? referrerId, decimal discountPct = 0)
    {
        EnsureValidDiscountPct(discountPct);

        return new DeliveryNote
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            WarehouseId = warehouseId,
            Code = DraftCode,
            Date = date,
            ExpectedDeliveryDate = expectedDeliveryDate,
            Reference = reference,
            TrackingNo = trackingNo,
            ShippingAddress = shippingAddress,
            ReferrerType = referrerType,
            ReferrerId = referrerId,
            Status = DeliveryNoteStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            DiscountPct = discountPct,
        };
    }

    public void UpdateHeader(
        Guid contactId, Guid warehouseId, DateOnly date, DateOnly expectedDeliveryDate, string? reference,
        string? trackingNo, string? shippingAddress, decimal discountPct)
    {
        EnsureDraft();
        EnsureValidDiscountPct(discountPct);
        ContactId = contactId;
        WarehouseId = warehouseId;
        Date = date;
        ExpectedDeliveryDate = expectedDeliveryDate;
        Reference = reference;
        TrackingNo = trackingNo;
        ShippingAddress = shippingAddress;
        DiscountPct = discountPct;
    }

    public void AddLine(
        Guid productId, decimal quantity, decimal rate, VatRate vatRate, decimal discountPct,
        Guid? unitId, decimal conversionFactor)
    {
        EnsureDraft();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("A delivery note line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        _lines.Add(DeliveryNoteLine.Create(
            Id, productId, quantity, rate, vatRate, discountPct, DiscountPct, unitId, conversionFactor));
    }

    public void ClearLines()
    {
        EnsureDraft();
        _lines.Clear();
    }

    public void Approve(Guid approvedByUserId, string code)
    {
        EnsureDraft();

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("A delivery note needs at least one line to be approved.");
        }

        Status = DeliveryNoteStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        if (Status != DeliveryNoteStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved delivery note can be voided.");
        }

        Status = DeliveryNoteStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Orthogonal to the lifecycle, exactly as on <see cref="SalesOrder.SetCustomStatus"/>.</summary>
    public void SetCustomStatus(Guid? customStatusId)
    {
        CustomStatusId = customStatusId;
    }

    /// <summary>Draft-only and sanitised on write -- see <see cref="SalesOrder.SetTerms"/>.</summary>
    public void SetTerms(string? terms)
    {
        EnsureDraft();
        Terms = RichText.Sanitize(terms);
    }

    /// <summary>Draft-only -- see <see cref="SalesOrder.SetCurrency"/>.</summary>
    public void SetCurrency(string? currencyCode, decimal? exchangeRate)
    {
        EnsureDraft();
        (CurrencyCode, ExchangeRate) = ExchangeRates.Validate(currencyCode, exchangeRate);
    }

    /// <summary>Draft-only -- see <see cref="SalesOrder.SetLocation"/>.</summary>
    public void SetLocation(Guid? locationId)
    {
        EnsureDraft();
        LocationId = locationId;
    }

    /// <summary>Phase 44's narrow backfill mutator -- see <see cref="SalesOrder.BackfillLocation"/>.</summary>
    public void BackfillLocation(Guid locationId)
    {
        if (LocationId is not null)
        {
            throw new InvalidOperationException(
                "This document already has a billing location; the backfill only fills in a missing one.");
        }

        LocationId = locationId;
    }

    private static void EnsureValidDiscountPct(decimal discountPct)
    {
        if (discountPct < 0 || discountPct > 100)
        {
            throw new InvalidOperationException("Discount% must be between 0 and 100.");
        }
    }

    private void EnsureDraft()
    {
        if (Status != DeliveryNoteStatus.Draft)
        {
            throw new InvalidOperationException("This delivery note is no longer in Draft status.");
        }
    }
}
