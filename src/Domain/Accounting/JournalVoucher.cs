namespace ErpApp.Domain.Accounting;

/// <summary>
/// The first real ApprovableTransaction (architecture-spec.md §3.2/§4.7): manual Debit/Credit
/// entries against any Account, Draft -> Approve. Code sits at the literal placeholder
/// <see cref="DraftCode"/> until Approve assigns the real IDocumentNumberGenerator-issued number
/// (architecture-spec.md §3.1 -- numbers assigned at Approve, not Create, confirmed live).
///
/// Lines is an encapsulated child collection (private backing field), same mapping shape as
/// Catalog.Product.SecondaryUnits. All mutation (AddLine/ClearLines/UpdateHeader) is Draft-only;
/// Approve() itself only flips Status/ApprovedBy/ApprovedAt/Code and enforces the
/// sum(Debit)==sum(Credit) invariant fast, before the Application-layer handler ever builds a
/// GlJournalEntry via IGlPostingRule -- Domain stays ignorant of the posting-rule abstraction,
/// which is an Application-layer concern (it doesn't need any I/O for JournalVoucher, but will for
/// later document types).
/// </summary>
public sealed class JournalVoucher
{
    public const string DraftCode = "DRAFT";

    private readonly List<JournalVoucherLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public JournalVoucherStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<JournalVoucherLine> Lines => _lines;

    private JournalVoucher()
    {
    }

    public static JournalVoucher Create(Guid organizationId, DateOnly date, string? reference)
    {
        return new JournalVoucher
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            Status = JournalVoucherStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateHeader(DateOnly date, string? reference)
    {
        EnsureDraft();
        Date = date;
        Reference = reference;
    }

    public void AddLine(Guid accountId, decimal debit, decimal credit)
    {
        EnsureDraft();

        if (debit < 0 || credit < 0 || (debit > 0 && credit > 0) || (debit == 0 && credit == 0))
        {
            throw new InvalidOperationException(
                "Each journal voucher line must have exactly one of Debit or Credit greater than zero.");
        }

        _lines.Add(JournalVoucherLine.Create(Id, accountId, debit, credit));
    }

    /// <summary>Full-replace of the line set -- the simplest correct approach for a client-driven
    /// multi-line editable table where the client always resubmits its whole current state.</summary>
    public void ClearLines()
    {
        EnsureDraft();
        _lines.Clear();
    }

    public void Approve(Guid approvedByUserId, string code)
    {
        EnsureDraft();

        if (_lines.Count < 2)
        {
            throw new InvalidOperationException("A journal voucher needs at least two lines to be approved.");
        }

        if (_lines.Sum(x => x.Debit) != _lines.Sum(x => x.Credit))
        {
            throw new InvalidOperationException(
                "A journal voucher's total Debit must equal its total Credit to be approved.");
        }

        Status = JournalVoucherStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    /// <summary>Terminal -- no edit, re-approve, or un-void afterward (roadmap Phase 16a). The
    /// document keeps its assigned Code; numbers are never recycled.</summary>
    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = JournalVoucherStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureDraft()
    {
        if (Status != JournalVoucherStatus.Draft)
        {
            throw new InvalidOperationException("This journal voucher is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != JournalVoucherStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved journal voucher can be voided.");
        }
    }
}
