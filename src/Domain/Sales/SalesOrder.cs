using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Sales;

/// <summary>
/// Standalone -- confirmed live NOT a conversion target of anything and not itself convertible
/// (architecture-spec.md §4.4/erp-module-scan.md's "Quotation -> Sales Order -> Invoice is not a
/// system-enforced 3-stage chain" correction). Cross-referenced only via free-text Reference (the
/// shared Reference field, same as every other document) -- no ReferrerType/ReferrerId. Same
/// Draft->Approve shape as Quotation, no GL/stock side effect on Approve.
/// </summary>
public sealed class SalesOrder
{
    public const string DraftCode = "DRAFT";

    private readonly List<SalesOrderLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public DateOnly? DeliveryDate { get; private set; }
    public string? Reference { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;
    public decimal DiscountPct { get; private set; }

    /// <summary>Phase 27a -- tenant-defined status pipeline (CustomStatus), orthogonal to the
    /// native Draft/Approved/Void lifecycle above. Live-confirmed on this type's own LIST grid:
    /// a "STAGE" column whose picker offers the tenant's
    /// Sales Order pipeline (Pending/Confirmed/Packaged/Delivered/Cancelled on the UAT tenant),
    /// settable on Draft and Approved rows alike. Deliberately not gated by EnsureDraft -- it
    /// carries no GL or stock weight, the same reasoning Phase 20b used for Quotation and Purchase
    /// Order, and 20a used for Custom Fields.</summary>
    public Guid? CustomStatusId { get; private set; }

    public IReadOnlyList<SalesOrderLine> Lines => _lines;

    private SalesOrder()
    {
    }

    public static SalesOrder Create(
        Guid organizationId, Guid contactId, DateOnly date, DateOnly? deliveryDate, string? reference, decimal discountPct = 0)
    {
        EnsureValidDiscountPct(discountPct);

        return new SalesOrder
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Code = DraftCode,
            Date = date,
            DeliveryDate = deliveryDate,
            Reference = reference,
            Status = SalesOrderStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            DiscountPct = discountPct,
        };
    }

    public void UpdateHeader(Guid contactId, DateOnly date, DateOnly? deliveryDate, string? reference, decimal discountPct)
    {
        EnsureDraft();
        EnsureValidDiscountPct(discountPct);
        ContactId = contactId;
        Date = date;
        DeliveryDate = deliveryDate;
        Reference = reference;
        DiscountPct = discountPct;
    }

    public void AddLine(Guid productId, decimal quantity, decimal rate, VatRate vatRate, decimal discountPct)
    {
        EnsureDraft();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("A sales order line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        _lines.Add(SalesOrderLine.Create(Id, productId, quantity, rate, vatRate, discountPct, DiscountPct));
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
            throw new InvalidOperationException("A sales order needs at least one line to be approved.");
        }

        Status = SalesOrderStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = SalesOrderStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Phase 27a -- see <see cref="CustomStatusId"/>. Setting or clearing (null) a pipeline
    /// value has no GL, stock or lifecycle side effect.</summary>
    public void SetCustomStatus(Guid? customStatusId)
    {
        CustomStatusId = customStatusId;
    }

    private void EnsureDraft()
    {
        if (Status != SalesOrderStatus.Draft)
        {
            throw new InvalidOperationException("This sales order is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != SalesOrderStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved sales order can be voided.");
        }
    }
}
