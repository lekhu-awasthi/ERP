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
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;
    public decimal DiscountPct { get; private set; }
    public Guid? CustomStatusId { get; private set; }

    /// <summary>Phase 27b -- the "+ Add Terms and Conditions" block's stored text (FR-11.3's
    /// CustomTemplate finding its first consumer). Free text on the document, <b>not</b> a pointer
    /// to the CustomTemplate it was seeded from: the reference product pre-fills the editor from a
    /// chosen template and then lets the user edit it freely (confirm-live 2026-09-03), so the
    /// template is a starting point, and a document must keep the words it was actually issued with
    /// even after that template is edited or deleted.</summary>
    public string? Terms { get; private set; }

    public IReadOnlyList<QuotationLine> Lines => _lines;

    private Quotation()
    {
    }

    public static Quotation Create(
        Guid organizationId, Guid contactId, DateOnly date, DateOnly? expiryDate, string? reference, decimal discountPct = 0)
    {
        EnsureValidDiscountPct(discountPct);

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
            DiscountPct = discountPct,
        };
    }

    public void UpdateHeader(Guid contactId, DateOnly date, DateOnly? expiryDate, string? reference, decimal discountPct)
    {
        EnsureDraft();
        EnsureValidDiscountPct(discountPct);
        ContactId = contactId;
        Date = date;
        ExpiryDate = expiryDate;
        Reference = reference;
        DiscountPct = discountPct;
    }

    public void AddLine(Guid productId, decimal quantity, decimal rate, VatRate vatRate, decimal discountPct)
    {
        EnsureDraft();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("A quotation line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        _lines.Add(QuotationLine.Create(Id, productId, quantity, rate, vatRate, discountPct, DiscountPct));
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
            throw new InvalidOperationException("A quotation needs at least one line to be approved.");
        }

        Status = QuotationStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void MarkConverted()
    {
        if (Status != QuotationStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved quotation can be converted to an Invoice.");
        }

        Status = QuotationStatus.Converted;
    }

    /// <summary>Only an Approved-not-yet-Converted quotation can be voided -- a Converted
    /// quotation has a live dependent (the Invoice created from it), so EnsureApproved's plain
    /// Status!=Approved check already rejects it (409) without a separate dependent-lookup: the
    /// Invoice itself, not this Quotation, is what would need voiding first (roadmap Phase 16a).</summary>
    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = QuotationStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Phase 20b -- tenant-defined status pipeline (CustomStatus), orthogonal to the
    /// Draft/Approved/Void/Converted lifecycle above (FR-12.2, live-confirmed against the real
    /// Tigg tenant: settable on both Draft and Approved rows, no interaction with Approve/Void/
    /// Convert). Not gated by EnsureDraft -- unlike header/line fields, this carries no GL/stock
    /// weight, same reasoning Phase 20a used for Custom Fields.</summary>
    public void SetCustomStatus(Guid? customStatusId)
    {
        CustomStatusId = customStatusId;
    }

    /// <summary>Draft-only, unlike <c>SetCustomStatus</c>: terms are part of what the document
    /// says, so they follow the same rule as every other header field rather than the
    /// orthogonal-metadata rule Custom Status follows.</summary>
    public void SetTerms(string? terms)
    {
        EnsureDraft();
        Terms = string.IsNullOrWhiteSpace(terms) ? null : terms.Trim();
    }

    private void EnsureDraft()
    {
        if (Status != QuotationStatus.Draft)
        {
            throw new InvalidOperationException("This quotation is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != QuotationStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved quotation can be voided.");
        }
    }
}
