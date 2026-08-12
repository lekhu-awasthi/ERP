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
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

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

    private void EnsureDraft()
    {
        if (Status != ExpenseStatus.Draft)
        {
            throw new InvalidOperationException("This expense is no longer in Draft status.");
        }
    }
}
