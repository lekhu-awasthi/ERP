using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.Sales;

/// <summary>
/// First real use of IGlPostingRule&lt;TDocument&gt; for a non-JournalVoucher/CashTransfer type,
/// and the first aggregate in this codebase with a required WarehouseId (architecture-spec.md
/// §3.5's "Warehouse is required specifically on Invoice and PurchaseBill" finding). Approve()
/// itself stays GL-ignorant, same split JournalVoucher established -- it only assigns the real
/// number and flips Status; the Application-layer ApproveInvoiceCommandHandler resolves each
/// line's Sales Account (Product.SalesAccountId, falling back to TenantSettings'
/// DefaultSalesAccountId) and calls IGlPostingRule&lt;InvoicePostingInput&gt; separately, since
/// that resolution needs DB reads Domain can't perform (see Application.Sales.Posting's doc
/// comments for the full reasoning).
///
/// ReferrerType/ReferrerId (architecture-spec.md §3.3) are set when this Invoice was created via
/// the Quotation-conversion flow -- null for a standalone Invoice.
///
/// Stock decrement is a deliberate no-op this phase (roadmap's own sequencing recommendation (a)):
/// Approve calls IStockAvailabilityPolicy, which is a literal always-Ok stub until Phase 7's real
/// FIFO ledger exists -- see Application.Sales.Stock.
/// </summary>
public sealed class Invoice
{
    public const string DraftCode = "DRAFT";

    private readonly List<InvoiceLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;
    public DocumentType? ReferrerType { get; private set; }
    public Guid? ReferrerId { get; private set; }
    public decimal DiscountPct { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public decimal GrandTotal => _lines.Sum(x => x.Amount + x.VatAmount);

    private Invoice()
    {
    }

    public static Invoice Create(
        Guid organizationId,
        Guid contactId,
        Guid warehouseId,
        DateOnly date,
        string? reference,
        DocumentType? referrerType,
        Guid? referrerId,
        decimal discountPct = 0)
    {
        EnsureValidDiscountPct(discountPct);

        return new Invoice
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            WarehouseId = warehouseId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            Status = InvoiceStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            ReferrerType = referrerType,
            ReferrerId = referrerId,
            DiscountPct = discountPct,
        };
    }

    public void UpdateHeader(Guid contactId, Guid warehouseId, DateOnly date, string? reference, decimal discountPct)
    {
        EnsureDraft();
        EnsureValidDiscountPct(discountPct);
        ContactId = contactId;
        WarehouseId = warehouseId;
        Date = date;
        Reference = reference;
        DiscountPct = discountPct;
    }

    public void AddLine(Guid productId, decimal quantity, decimal rate, VatRate vatRate, decimal discountPct)
    {
        EnsureDraft();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("An invoice line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        _lines.Add(InvoiceLine.Create(Id, productId, quantity, rate, vatRate, discountPct, DiscountPct));
    }

    private static void EnsureValidDiscountPct(decimal discountPct)
    {
        if (discountPct < 0 || discountPct > 100)
        {
            throw new InvalidOperationException("Discount% must be between 0 and 100.");
        }
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
            throw new InvalidOperationException("An invoice needs at least one line to be approved.");
        }

        Status = InvoiceStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = InvoiceStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureDraft()
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException("This invoice is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != InvoiceStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved invoice can be voided.");
        }
    }
}
