using ErpApp.Domain.Common;

namespace ErpApp.Domain.Purchasing;

/// <summary>
/// NOT a clone of PurchaseBill -- confirmed live as its own document type with no Product lines at
/// all, an "Accounts" line-item table instead (erp-module-scan.md's Purchase Module > Expenses
/// section). Same Draft->Approve ApprovableTransaction shape and TDS fields as PurchaseBill
/// (TdsApplicable toggle confirmed live; TdsAmount resolved server-side from TdsType.RatePct, same
/// reasoning as PurchaseBill.TdsAmount).
/// </summary>
public sealed class Expense
{
    public const string DraftCode = "DRAFT";

    private readonly List<ExpenseLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public string? SupplierInvoiceReference { get; private set; }
    public string? Notes { get; private set; }
    public bool TdsApplicable { get; private set; }
    public Guid? TdsTypeId { get; private set; }
    public decimal TdsAmount { get; private set; }
    public ExpenseStatus Status { get; private set; }
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

    public IReadOnlyList<ExpenseLine> Lines => _lines;

    public decimal GrandTotal => _lines.Sum(x => x.Amount + x.VatAmount);

    private Expense()
    {
    }

    public static Expense Create(
        Guid organizationId,
        Guid contactId,
        DateOnly date,
        DateOnly? dueDate,
        string? supplierInvoiceReference,
        string? notes,
        bool tdsApplicable,
        Guid? tdsTypeId,
        decimal tdsAmount)
    {
        return new Expense
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Code = DraftCode,
            Date = date,
            DueDate = dueDate,
            SupplierInvoiceReference = supplierInvoiceReference,
            Notes = notes,
            TdsApplicable = tdsApplicable,
            TdsTypeId = tdsApplicable ? tdsTypeId : null,
            TdsAmount = tdsApplicable ? tdsAmount : 0,
            Status = ExpenseStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateHeader(
        Guid contactId,
        DateOnly date,
        DateOnly? dueDate,
        string? supplierInvoiceReference,
        string? notes,
        bool tdsApplicable,
        Guid? tdsTypeId,
        decimal tdsAmount)
    {
        EnsureDraft();
        ContactId = contactId;
        Date = date;
        DueDate = dueDate;
        SupplierInvoiceReference = supplierInvoiceReference;
        Notes = notes;
        TdsApplicable = tdsApplicable;
        TdsTypeId = tdsApplicable ? tdsTypeId : null;
        TdsAmount = tdsApplicable ? tdsAmount : 0;
    }

    public void AddLine(Guid accountId, decimal amount, Catalog.VatRate vatRate)
    {
        EnsureDraft();

        if (amount <= 0)
        {
            throw new InvalidOperationException("An expense line needs a positive Amount.");
        }

        _lines.Add(ExpenseLine.Create(Id, accountId, amount, vatRate));
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
            throw new InvalidOperationException("An expense needs at least one line to be approved.");
        }

        Status = ExpenseStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = ExpenseStatus.Void;
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
        if (Status != ExpenseStatus.Draft)
        {
            throw new InvalidOperationException("This expense is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != ExpenseStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved expense can be voided.");
        }
    }
}
