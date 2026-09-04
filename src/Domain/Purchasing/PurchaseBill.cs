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
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>
    /// Phase 28 (FR-2.5). The currency this document's own amounts are denominated in -- the
    /// three-letter code, not a Currency row's id (see Domain.Tenancy.Currency for why). Defaults
    /// to the base currency, so every document created before this phase, and every document a
    /// single-currency tenant will ever create, needs no special handling anywhere.
    /// </summary>
    public string CurrencyCode { get; private set; } = CurrencyCatalog.BaseCode;

    /// <summary>
    /// This document's rate to the base currency, stored on the document rather than looked up by
    /// date. Confirmed live 2026-09-04: the reference product's "Exchange Rate To NPR*" is a plain
    /// manual number input with no date coupling, and its conversion flow carries the rate along in
    /// the pre-fill snapshot rather than re-deriving it. Exactly 1 for a base-currency document --
    /// an invariant enforced by <see cref="ExchangeRates.Validate"/>, matching the live form, which
    /// disables the input and pins it to 1 whenever the selected currency is NPR.
    /// </summary>
    public decimal ExchangeRate { get; private set; } = ExchangeRates.BaseRate;
    public DocumentType? ReferrerType { get; private set; }
    public Guid? ReferrerId { get; private set; }
    public decimal DiscountPct { get; private set; }

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
        Guid? referrerId,
        decimal discountPct = 0)
    {
        EnsureValidDiscountPct(discountPct);

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
            DiscountPct = discountPct,
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
        decimal tdsAmount,
        decimal discountPct)
    {
        EnsureDraft();
        EnsureValidDiscountPct(discountPct);
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
        DiscountPct = discountPct;
    }

    public void AddLine(
        Guid productId, decimal quantity, decimal rate, VatRate vatRate, ExpenditureClassification expenditureClassification,
        decimal discountPct)
    {
        EnsureDraft();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("A purchase bill line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        _lines.Add(PurchaseBillLine.Create(Id, productId, quantity, rate, vatRate, expenditureClassification, discountPct, DiscountPct));
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
            throw new InvalidOperationException("A purchase bill needs at least one line to be approved.");
        }

        Status = PurchaseBillStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = PurchaseBillStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Sets this document's transaction currency and its rate to the base currency. A separate
    /// mutator rather than two more parameters on Create/UpdateHeader, for the same reason
    /// <c>SetExport</c> is one: it is an orthogonal facet of the header with its own invariant
    /// (<see cref="ExchangeRates.Validate"/>), and threading it through every constructor would
    /// change twelve aggregates' signatures to express one fact. Draft-only, like every other
    /// header mutation -- an Approved document's amounts are already posted to the general ledger
    /// at its rate, so changing that rate afterwards would silently invalidate the posting.
    /// </summary>
    public void SetCurrency(string? currencyCode, decimal? exchangeRate)
    {
        EnsureDraft();
        (CurrencyCode, ExchangeRate) = ExchangeRates.Validate(currencyCode, exchangeRate);
    }

    private void EnsureDraft()
    {
        if (Status != PurchaseBillStatus.Draft)
        {
            throw new InvalidOperationException("This purchase bill is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != PurchaseBillStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved purchase bill can be voided.");
        }
    }
}
