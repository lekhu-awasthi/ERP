using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.Purchasing;

/// <summary>
/// Phase 58 -- a Goods Received Note: goods arriving from a supplier into one warehouse, recorded
/// apart from the Purchase Bill that is owed for them. The purchase side of the <b>physical</b>
/// stock ledger (see <c>Domain.Inventory.StockBooks</c>).
///
/// <para><b>What Approve does, and what it does not.</b> It draws the document number and writes one
/// <c>PhysicalStockMovement</c> In row per Goods line. It creates no FIFO layer and posts nothing to
/// the general ledger -- confirmed live 2026-09-24: after a GRN and its bill, the reference
/// product's Trial Balance held the bill and nothing else. So there is no goods-received-not-billed
/// account to clear, and a tenant's accounting stock does not move until the Purchase Bill is
/// approved, whichever mode it runs in.</para>
///
/// <para><b>Shape.</b> The <see cref="PurchaseOrder"/> header plus a warehouse and a tracking
/// number -- the live form's fields, minus the per-line warehouse, which this codebase's purchase
/// documents do not carry (the Purchase Bill has one warehouse for the whole document, and the
/// reference tenant cannot turn multiple warehouses on to show what the column would do). No Terms
/// and Conditions and no Additional Cost: the live form has neither.</para>
///
/// <para><b>Converted from a Purchase Order, never into anything.</b> <see cref="ReferrerType"/> and
/// <see cref="ReferrerId"/> name the order it was received against; the Create handler marks that
/// order received. Receiving and billing are independent in the reference product (a PO line keeps
/// a <c>received_quantity</c> and a <c>billed_quantity</c> of its own), which is why this is a
/// second flag on the order rather than a use of its Converted status.</para>
/// </summary>
public sealed class GoodsReceivedNote
{
    public const string DraftCode = "DRAFT";

    private readonly List<GoodsReceivedNoteLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }

    /// <summary>The carrier's or supplier's consignment number -- the live form's "Tracking No",
    /// free text, shown in the list grid.</summary>
    public string? TrackingNo { get; private set; }

    public GoodsReceivedNoteStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>Phase 28's currency pair, carried because the live GRN form carries it. Nothing is
    /// converted into the GL at Approve, because nothing is posted; it is what the document says.</summary>
    public string CurrencyCode { get; private set; } = CurrencyCatalog.BaseCode;

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal ExchangeRate { get; private set; } = ExchangeRates.BaseRate;

    /// <summary>Phase 32's billing location -- see <c>PurchaseOrder.LocationId</c>.</summary>
    public Guid? LocationId { get; private set; }

    public decimal DiscountPct { get; private set; }

    /// <summary>Phase 20b's list-grid Stage picker. The reference product seeds four GRN statuses
    /// (Pending / Received / Partially Received / Cancelled) behind a Stage column.</summary>
    public Guid? CustomStatusId { get; private set; }

    public DocumentType? ReferrerType { get; private set; }
    public Guid? ReferrerId { get; private set; }

    public IReadOnlyList<GoodsReceivedNoteLine> Lines => _lines;

    private GoodsReceivedNote()
    {
    }

    public static GoodsReceivedNote Create(
        Guid organizationId, Guid contactId, Guid warehouseId, DateOnly date, string? reference,
        string? trackingNo, DocumentType? referrerType, Guid? referrerId, decimal discountPct = 0)
    {
        EnsureValidDiscountPct(discountPct);

        return new GoodsReceivedNote
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            WarehouseId = warehouseId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            TrackingNo = trackingNo,
            ReferrerType = referrerType,
            ReferrerId = referrerId,
            Status = GoodsReceivedNoteStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            DiscountPct = discountPct,
        };
    }

    public void UpdateHeader(
        Guid contactId, Guid warehouseId, DateOnly date, string? reference, string? trackingNo, decimal discountPct)
    {
        EnsureDraft();
        EnsureValidDiscountPct(discountPct);
        ContactId = contactId;
        WarehouseId = warehouseId;
        Date = date;
        Reference = reference;
        TrackingNo = trackingNo;
        DiscountPct = discountPct;
    }

    public void AddLine(
        Guid productId, decimal quantity, decimal rate, VatRate vatRate, decimal discountPct,
        Guid? unitId, decimal conversionFactor)
    {
        EnsureDraft();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("A goods received note line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        _lines.Add(GoodsReceivedNoteLine.Create(
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
            throw new InvalidOperationException("A goods received note needs at least one line to be approved.");
        }

        Status = GoodsReceivedNoteStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        if (Status != GoodsReceivedNoteStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved goods received note can be voided.");
        }

        Status = GoodsReceivedNoteStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Orthogonal to the lifecycle, exactly as on <see cref="PurchaseOrder.SetCustomStatus"/>.</summary>
    public void SetCustomStatus(Guid? customStatusId)
    {
        CustomStatusId = customStatusId;
    }

    /// <summary>Draft-only -- see <see cref="PurchaseOrder.SetCurrency"/>.</summary>
    public void SetCurrency(string? currencyCode, decimal? exchangeRate)
    {
        EnsureDraft();
        (CurrencyCode, ExchangeRate) = ExchangeRates.Validate(currencyCode, exchangeRate);
    }

    /// <summary>Draft-only -- see <see cref="PurchaseOrder.SetLocation"/>.</summary>
    public void SetLocation(Guid? locationId)
    {
        EnsureDraft();
        LocationId = locationId;
    }

    /// <summary>Phase 44's narrow backfill mutator -- see <see cref="PurchaseOrder.BackfillLocation"/>.</summary>
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
        if (Status != GoodsReceivedNoteStatus.Draft)
        {
            throw new InvalidOperationException("This goods received note is no longer in Draft status.");
        }
    }
}
