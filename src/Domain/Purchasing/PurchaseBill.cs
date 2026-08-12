using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.Purchasing;

/// <summary>
/// Clones Sales.Invoice's shape (WarehouseId required, first-required-on-stock-moving-documents
/// pattern) plus Purchase-specific fields confirmed live in erp-module-scan.md's Purchase Bills
/// section: SupplierInvoiceReference (the supplier's own bill number), IsImport + Import Details
/// (ImportCountry/ImportDate/ImportDocumentNo, only meaningful when IsImport=true -- modeled as
/// nullable regardless, validated required-when-IsImport at the Application layer, same
/// "optional-unless-a-flag-turns-it-on" pattern used elsewhere), TdsTypeId + TdsAmount (TdsAmount
/// is resolved server-side by the Application handler from TdsType.RatePct -- fetching that rate
/// is a DB read, so it's computed before Create/UpdateHeader is called, not inside Domain).
///
/// ReferrerType/ReferrerId are set when this PurchaseBill was created via the
/// PurchaseOrder-conversion flow -- null for a standalone PurchaseBill.
///
/// Stock increment is a deliberate no-op this phase, same as Invoice's decrement stub -- see
/// Application.Purchasing.Stock's doc comment.
/// </summary>
public sealed class PurchaseBill
{
    public const string DraftCode = "DRAFT";

    private readonly List<PurchaseBillLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public string? SupplierInvoiceReference { get; private set; }
    public bool IsImport { get; private set; }
    public string? ImportCountry { get; private set; }
    public DateOnly? ImportDate { get; private set; }
    public string? ImportDocumentNo { get; private set; }
    public Guid? TdsTypeId { get; private set; }
    public decimal TdsAmount { get; private set; }
    public PurchaseBillStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;
    public DocumentType? ReferrerType { get; private set; }
    public Guid? ReferrerId { get; private set; }

    public IReadOnlyList<PurchaseBillLine> Lines => _lines;

    public decimal GrandTotal => _lines.Sum(x => x.Amount + x.VatAmount);

    private PurchaseBill()
    {
    }

    public static PurchaseBill Create(
        Guid organizationId,
        Guid contactId,
        Guid warehouseId,
        DateOnly date,
        string? reference,
        string? supplierInvoiceReference,
        bool isImport,
        string? importCountry,
        DateOnly? importDate,
        string? importDocumentNo,
        Guid? tdsTypeId,
        decimal tdsAmount,
        DocumentType? referrerType,
        Guid? referrerId)
    {
        return new PurchaseBill
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            WarehouseId = warehouseId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            SupplierInvoiceReference = supplierInvoiceReference,
            IsImport = isImport,
            ImportCountry = isImport ? importCountry : null,
            ImportDate = isImport ? importDate : null,
            ImportDocumentNo = isImport ? importDocumentNo : null,
            TdsTypeId = tdsTypeId,
            TdsAmount = tdsAmount,
            Status = PurchaseBillStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            ReferrerType = referrerType,
            ReferrerId = referrerId,
        };
    }

    public void UpdateHeader(
        Guid contactId,
        Guid warehouseId,
        DateOnly date,
        string? reference,
        string? supplierInvoiceReference,
        bool isImport,
        string? importCountry,
        DateOnly? importDate,
        string? importDocumentNo,
        Guid? tdsTypeId,
        decimal tdsAmount)
    {
        EnsureDraft();
        ContactId = contactId;
        WarehouseId = warehouseId;
        Date = date;
        Reference = reference;
        SupplierInvoiceReference = supplierInvoiceReference;
        IsImport = isImport;
        ImportCountry = isImport ? importCountry : null;
        ImportDate = isImport ? importDate : null;
        ImportDocumentNo = isImport ? importDocumentNo : null;
        TdsTypeId = tdsTypeId;
        TdsAmount = tdsAmount;
    }

    public void AddLine(
        Guid productId, decimal quantity, decimal rate, VatRate vatRate, ExpenditureClassification expenditureClassification)
    {
        EnsureDraft();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("A purchase bill line needs a positive Quantity and a non-negative Rate.");
        }

        _lines.Add(PurchaseBillLine.Create(Id, productId, quantity, rate, vatRate, expenditureClassification));
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
            throw new InvalidOperationException("A purchase bill needs at least one line to be approved.");
        }

        Status = PurchaseBillStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    private void EnsureDraft()
    {
        if (Status != PurchaseBillStatus.Draft)
        {
            throw new InvalidOperationException("This purchase bill is no longer in Draft status.");
        }
    }
}
