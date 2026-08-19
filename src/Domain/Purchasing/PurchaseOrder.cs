using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Purchasing;

/// <summary>
/// First Purchasing-context ApprovableTransaction (architecture-spec.md §4.5) -- clones
/// Sales.Quotation's shape exactly (Draft->Approve, Code sits at DraftCode until Approve assigns
/// the real IDocumentNumberGenerator-issued number). No GL/stock side effect on Approve --
/// confirmed live ("No negative-stock validation triggered on PO approval - confirms PO genuinely
/// does not move stock", erp-module-scan.md's hands-on pass item 7). ContactId is filtered to
/// ContactType.Supplier by PurchasingValidation, not Customer.
/// </summary>
public sealed class PurchaseOrder
{
    public const string DraftCode = "DRAFT";

    private readonly List<PurchaseOrderLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

    private PurchaseOrder()
    {
    }

    public static PurchaseOrder Create(Guid organizationId, Guid contactId, DateOnly date, string? reference)
    {
        return new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            Status = PurchaseOrderStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateHeader(Guid contactId, DateOnly date, string? reference)
    {
        EnsureDraft();
        ContactId = contactId;
        Date = date;
        Reference = reference;
    }

    public void AddLine(Guid productId, decimal quantity, decimal rate, VatRate vatRate)
    {
        EnsureDraft();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("A purchase order line needs a positive Quantity and a non-negative Rate.");
        }

        _lines.Add(PurchaseOrderLine.Create(Id, productId, quantity, rate, vatRate));
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
            throw new InvalidOperationException("A purchase order needs at least one line to be approved.");
        }

        Status = PurchaseOrderStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void MarkConverted()
    {
        if (Status != PurchaseOrderStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved purchase order can be converted to a Purchase Bill.");
        }

        Status = PurchaseOrderStatus.Converted;
    }

    /// <summary>Mirror of Quotation.Void -- a Converted purchase order (live dependent: the
    /// PurchaseBill created from it) is rejected by EnsureApproved's plain status check.</summary>
    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = PurchaseOrderStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureDraft()
    {
        if (Status != PurchaseOrderStatus.Draft)
        {
            throw new InvalidOperationException("This purchase order is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != PurchaseOrderStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved purchase order can be voided.");
        }
    }
}
