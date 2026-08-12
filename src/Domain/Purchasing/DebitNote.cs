using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.Purchasing;

/// <summary>
/// Mirror of Sales.CreditNote -- conversion target of PurchaseBill (architecture-spec.md §3.3/
/// §4.5), same pattern PurchaseBill established for PurchaseOrder. Approve() posts the exact
/// reverse of PurchaseBillPostingRule (Debit Accounts Payable, Credit each line's Purchase
/// Account, Credit VAT Receivable) via DebitNotePostingRule, same resolved-input-record split
/// PurchaseBillPostingInput uses. No TDS fields -- a reversal of a bill doesn't reverse the TDS
/// withholding (out of scope this phase, same "don't build what wasn't confirmed" judgment as
/// CreditNote's own scope).
/// </summary>
public sealed class DebitNote
{
    public const string DraftCode = "DRAFT";

    private readonly List<DebitNoteLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public DebitNoteStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;
    public DocumentType? ReferrerType { get; private set; }
    public Guid? ReferrerId { get; private set; }

    public IReadOnlyList<DebitNoteLine> Lines => _lines;

    private DebitNote()
    {
    }

    public static DebitNote Create(
        Guid organizationId, Guid contactId, DateOnly date, string? reference, DocumentType? referrerType, Guid? referrerId)
    {
        return new DebitNote
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            Status = DebitNoteStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            ReferrerType = referrerType,
            ReferrerId = referrerId,
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
            throw new InvalidOperationException("A debit note line needs a positive Quantity and a non-negative Rate.");
        }

        _lines.Add(DebitNoteLine.Create(Id, productId, quantity, rate, vatRate));
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
            throw new InvalidOperationException("A debit note needs at least one line to be approved.");
        }

        Status = DebitNoteStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    private void EnsureDraft()
    {
        if (Status != DebitNoteStatus.Draft)
        {
            throw new InvalidOperationException("This debit note is no longer in Draft status.");
        }
    }
}
