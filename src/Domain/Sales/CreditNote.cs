using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.Sales;

/// <summary>
/// Conversion target of Invoice (architecture-spec.md §3.3/§4.4), same pattern Invoice already
/// established for Quotation -- ReferrerType/ReferrerId point back at the source Invoice.
/// Approve() posts the exact reverse of InvoicePostingRule (Credit Accounts Receivable, Debit
/// each line's Sales Revenue account, Debit VAT Payable) via CreditNotePostingRule, same
/// resolved-input-record split InvoicePostingInput uses.
/// </summary>
public sealed class CreditNote
{
    public const string DraftCode = "DRAFT";

    private readonly List<CreditNoteLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public CreditNoteStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;
    public DocumentType? ReferrerType { get; private set; }
    public Guid? ReferrerId { get; private set; }

    public IReadOnlyList<CreditNoteLine> Lines => _lines;

    private CreditNote()
    {
    }

    public static CreditNote Create(
        Guid organizationId, Guid contactId, DateOnly date, string? reference, DocumentType? referrerType, Guid? referrerId)
    {
        return new CreditNote
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            Status = CreditNoteStatus.Draft,
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
            throw new InvalidOperationException("A credit note line needs a positive Quantity and a non-negative Rate.");
        }

        _lines.Add(CreditNoteLine.Create(Id, productId, quantity, rate, vatRate));
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
            throw new InvalidOperationException("A credit note needs at least one line to be approved.");
        }

        Status = CreditNoteStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    private void EnsureDraft()
    {
        if (Status != CreditNoteStatus.Draft)
        {
            throw new InvalidOperationException("This credit note is no longer in Draft status.");
        }
    }
}
