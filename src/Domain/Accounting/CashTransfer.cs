namespace ErpApp.Domain.Accounting;

/// <summary>
/// Simplified UI over JournalVoucher (architecture-spec.md §4.7) -- one FromAccountId, N
/// (ToAccountId, Amount) destination rows (fan-out confirmed live in the reference product).
/// Internally still posts as one balanced multi-line GL entry through the same
/// IGlPostingRule&lt;CashTransfer&gt;/GlJournalEntry.Post() path JournalVoucher uses -- see
/// CashTransferPostingRule (Application layer), not a parallel posting path.
///
/// Same Draft->Approve/RowVersion/encapsulated-Lines shape as JournalVoucher; see that type's doc
/// comment for the shared rationale.
/// </summary>
public sealed class CashTransfer
{
    public const string DraftCode = "DRAFT";

    private readonly List<CashTransferLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public Guid FromAccountId { get; private set; }
    public CashTransferStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<CashTransferLine> Lines => _lines;

    private CashTransfer()
    {
    }

    public static CashTransfer Create(Guid organizationId, DateOnly date, string? reference, Guid fromAccountId)
    {
        return new CashTransfer
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            FromAccountId = fromAccountId,
            Status = CashTransferStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateHeader(DateOnly date, string? reference, Guid fromAccountId)
    {
        EnsureDraft();
        Date = date;
        Reference = reference;
        FromAccountId = fromAccountId;
    }

    public void AddLine(Guid toAccountId, decimal amount)
    {
        EnsureDraft();

        if (amount <= 0)
        {
            throw new InvalidOperationException("Each cash transfer destination line must have an Amount greater than zero.");
        }

        _lines.Add(CashTransferLine.Create(Id, toAccountId, amount));
    }

    public void ClearLines()
    {
        EnsureDraft();
        _lines.Clear();
    }

    public void Approve(Guid approvedByUserId, string code)
    {
        EnsureDraft();

        if (_lines.Count < 1)
        {
            throw new InvalidOperationException("A cash transfer needs at least one destination line to be approved.");
        }

        if (_lines.Any(x => x.ToAccountId == FromAccountId))
        {
            throw new InvalidOperationException("A cash transfer's destination accounts must differ from its From account.");
        }

        Status = CashTransferStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = CashTransferStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureDraft()
    {
        if (Status != CashTransferStatus.Draft)
        {
            throw new InvalidOperationException("This cash transfer is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != CashTransferStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved cash transfer can be voided.");
        }
    }
}
