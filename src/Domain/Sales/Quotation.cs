using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Sales;

/// <summary>
/// First Sales-context ApprovableTransaction (architecture-spec.md §4.4) -- same Draft->Approve
/// shape as Accounting.JournalVoucher (Code sits at DraftCode until Approve assigns the real
/// IDocumentNumberGenerator-issued number). No GL/stock side effect on Approve -- confirmed live,
/// same as PurchaseOrder's "planning document" note (architecture-spec.md §4.5). Approve just
/// needs at least one line; unlike JournalVoucher there's no balance invariant to check.
/// </summary>
public sealed class Quotation
{
    public const string DraftCode = "DRAFT";

    private readonly List<QuotationLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public string? Reference { get; private set; }
    public QuotationStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<QuotationLine> Lines => _lines;

    private Quotation()
    {
    }

    public static Quotation Create(
        Guid organizationId, Guid contactId, DateOnly date, DateOnly? expiryDate, string? reference)
    {
        return new Quotation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Code = DraftCode,
            Date = date,
            ExpiryDate = expiryDate,
            Reference = reference,
            Status = QuotationStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateHeader(Guid contactId, DateOnly date, DateOnly? expiryDate, string? reference)
    {
        EnsureDraft();
        ContactId = contactId;
        Date = date;
        ExpiryDate = expiryDate;
        Reference = reference;
    }

    public void AddLine(Guid productId, decimal quantity, decimal rate, VatRate vatRate)
    {
        EnsureDraft();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("A quotation line needs a positive Quantity and a non-negative Rate.");
        }

        _lines.Add(QuotationLine.Create(Id, productId, quantity, rate, vatRate));
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
            throw new InvalidOperationException("A quotation needs at least one line to be approved.");
        }

        Status = QuotationStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    private void EnsureDraft()
    {
        if (Status != QuotationStatus.Draft)
        {
            throw new InvalidOperationException("This quotation is no longer in Draft status.");
        }
    }
}
