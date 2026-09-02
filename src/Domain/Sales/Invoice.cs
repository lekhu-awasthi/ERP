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
///
/// <para><b>Export sales (FR-5.8, Phase 23).</b> IsExport + ExportCountry/ExportDeclarationNo/
/// ExportDeclarationDate mirror PurchaseBill's existing IsImport block, and like it the detail
/// fields are nullable regardless and only meaningful when the flag is set. Two differences from
/// that block, both live-confirmed against the reference product rather than assumed:</para>
/// <para>1. The detail fields are <b>optional even when the flag is set</b> -- the live form marks
/// Customer/Date/Due Date/Warehouse with a required asterisk and pointedly does not mark Country,
/// Date or Document No. PurchaseBill's import fields are required-when-flagged; this is not.</para>
/// <para>2. <b>An export sale is zero-rated, and the aggregate enforces it.</b> On the live form,
/// ticking "This is export sales" disables the per-line Tax selector outright and pins every line
/// to "0 Vat" (verified in the DOM: the control carries ant-select-disabled). So SetExport and
/// AddLine both coerce every line's VatRate to ZeroVat -- putting the rule in the aggregate rather
/// than in a validator or the Angular form, because it is an invariant of the document and not a
/// property of one entry path. Note ZeroVat (zero-rated) is deliberately not NoVat (exempt): both
/// compute 0 VAT, but they are different statutory buckets and VAT Summary reports them separately.
/// </para>
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
    public bool IsExport { get; private set; }
    public string? ExportCountry { get; private set; }
    public string? ExportDeclarationNo { get; private set; }
    public DateOnly? ExportDeclarationDate { get; private set; }
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
        decimal discountPct = 0,
        bool isExport = false,
        string? exportCountry = null,
        string? exportDeclarationNo = null,
        DateOnly? exportDeclarationDate = null)
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
            IsExport = isExport,
            ExportCountry = isExport ? exportCountry : null,
            ExportDeclarationNo = isExport ? exportDeclarationNo : null,
            ExportDeclarationDate = isExport ? exportDeclarationDate : null,
            Status = InvoiceStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            ReferrerType = referrerType,
            ReferrerId = referrerId,
            DiscountPct = discountPct,
        };
    }

    public void UpdateHeader(
        Guid contactId,
        Guid warehouseId,
        DateOnly date,
        string? reference,
        decimal discountPct,
        bool isExport = false,
        string? exportCountry = null,
        string? exportDeclarationNo = null,
        DateOnly? exportDeclarationDate = null)
    {
        EnsureDraft();
        EnsureValidDiscountPct(discountPct);
        ContactId = contactId;
        WarehouseId = warehouseId;
        Date = date;
        Reference = reference;
        DiscountPct = discountPct;
        SetExport(isExport, exportCountry, exportDeclarationNo, exportDeclarationDate);
    }

    /// <summary>Turning the export flag on <b>re-rates every line already on the document</b> to
    /// ZeroVat, and turning it off leaves them alone -- the user picks the rate again. Rebuilding
    /// the lines is what keeps "an export invoice is zero-rated" true no matter which order the
    /// user ticks the box and adds lines in; AddLine covers the other order.</summary>
    public void SetExport(
        bool isExport, string? exportCountry, string? exportDeclarationNo, DateOnly? exportDeclarationDate)
    {
        EnsureDraft();
        IsExport = isExport;
        ExportCountry = isExport ? exportCountry : null;
        ExportDeclarationNo = isExport ? exportDeclarationNo : null;
        ExportDeclarationDate = isExport ? exportDeclarationDate : null;

        if (!isExport)
        {
            return;
        }

        var existing = _lines.ToList();
        _lines.Clear();
        foreach (var line in existing)
        {
            _lines.Add(InvoiceLine.Create(
                Id, line.ProductId, line.Quantity, line.Rate, VatRate.ZeroVat, line.DiscountPct, DiscountPct));
        }
    }

    public void AddLine(Guid productId, decimal quantity, decimal rate, VatRate vatRate, decimal discountPct)
    {
        EnsureDraft();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("An invoice line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        // An export sale is zero-rated: the live reference product disables the line's Tax selector
        // entirely when the flag is set, so a caller's choice here is not merely overridden, it was
        // never offered. Enforced in the aggregate so no entry path can bypass it.
        var effectiveVatRate = IsExport ? VatRate.ZeroVat : vatRate;

        _lines.Add(InvoiceLine.Create(Id, productId, quantity, rate, effectiveVatRate, discountPct, DiscountPct));
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
