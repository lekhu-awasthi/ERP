using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.Purchasing;

/// <summary>
/// First Purchasing-context ApprovableTransaction (architecture-spec.md §4.5) -- clones
/// Sales.Quotation's shape exactly (Draft->Approve, Code sits at DraftCode until Approve assigns
/// the real IDocumentNumberGenerator-issued number). No GL/stock side effect on Approve --
/// confirmed live ("No negative-stock validation triggered on PO approval - confirms PO genuinely
/// does not move stock", erp-module-scan.md's hands-on pass item 7). ContactId is filtered to
/// ContactType.Supplier by PurchasingValidation, not Customer.
/// </summary>
public sealed class PurchaseOrder
{
    public const string DraftCode = "DRAFT";

    private readonly List<PurchaseOrderLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
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
    /// Phase 32 (FR-2.3/FR-3.3). The billing location this document was raised from -- the
    /// borderless picker in the live document header, left of Save, rendering "Name (Code)" and
    /// defaulting to HeadOffice (confirmed live 2026-09-07 on a location-enabled tenant).
    ///
    /// <para>Nullable, and nullable on every transactional aggregate rather than only the sales-side
    /// ones, because <see cref="Domain.Tenancy.LocationScopeMode"/> is a runtime setting an Admin can
    /// widen at any moment: the column has to exist before the switch is flipped, or the switch is a
    /// lie. <see cref="Domain.Tenancy.DocumentLocationScope"/> is the single place that decides
    /// whether a given type carries one for a given tenant.</para>
    ///
    /// <para>An Id rather than a code, unlike <see cref="CurrencyCode"/> directly above -- see
    /// Domain.Tenancy.BillingLocation for why the two diverge.</para>
    /// </summary>
    public Guid? LocationId { get; private set; }

    /// <summary>
    /// This document's rate to the base currency, stored on the document rather than looked up by
    /// date. Confirmed live 2026-09-04: the reference product's "Exchange Rate To NPR*" is a plain
    /// manual number input with no date coupling, and its conversion flow carries the rate along in
    /// the pre-fill snapshot rather than re-deriving it. Exactly 1 for a base-currency document --
    /// an invariant enforced by <see cref="ExchangeRates.Validate"/>, matching the live form, which
    /// disables the input and pins it to 1 whenever the selected currency is NPR.
    /// </summary>
    public decimal ExchangeRate { get; private set; } = ExchangeRates.BaseRate;
    public decimal DiscountPct { get; private set; }
    public Guid? CustomStatusId { get; private set; }

    /// <summary>Phase 27b -- the "+ Add Terms and Conditions" block's stored text (FR-11.3's
    /// CustomTemplate finding its first consumer). Free text on the document, <b>not</b> a pointer
    /// to the CustomTemplate it was seeded from: the reference product pre-fills the editor from a
    /// chosen template and then lets the user edit it freely (confirm-live 2026-09-03), so the
    /// template is a starting point, and a document must keep the words it was actually issued with
    /// even after that template is edited or deleted.</summary>
    public string? Terms { get; private set; }

    /// <summary>
    /// Phase 58 -- when a Goods Received Note was created against this order, or null. A second
    /// conversion flag beside <see cref="PurchaseOrderStatus.Converted"/>, not a new status value,
    /// because receiving and billing are independent: the reference product keeps a
    /// <c>received_quantity</c> and a <c>billed_quantity</c> on every PO line, and after a full
    /// receipt it still offered Convert to Bill with the whole quantity (confirmed live 2026-09-24).
    /// A status can hold only one of the two facts; this order can have both.
    /// </summary>
    public DateTimeOffset? ReceivedAt { get; private set; }

    public bool IsReceived => ReceivedAt is not null;

    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

    private PurchaseOrder()
    {
    }

    public static PurchaseOrder Create(Guid organizationId, Guid contactId, DateOnly date, string? reference, decimal discountPct = 0)
    {
        EnsureValidDiscountPct(discountPct);

        return new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            Status = PurchaseOrderStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            DiscountPct = discountPct,
        };
    }

    public void UpdateHeader(Guid contactId, DateOnly date, string? reference, decimal discountPct)
    {
        EnsureDraft();
        EnsureValidDiscountPct(discountPct);
        ContactId = contactId;
        Date = date;
        Reference = reference;
        DiscountPct = discountPct;
    }

    public void AddLine(
        Guid productId, decimal quantity, decimal rate, VatRate vatRate, decimal discountPct,
        Guid? unitId, decimal conversionFactor)
    {
        EnsureDraft();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("A purchase order line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        _lines.Add(PurchaseOrderLine.Create(
            Id, productId, quantity, rate, vatRate, discountPct, DiscountPct, unitId, conversionFactor));
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
            throw new InvalidOperationException("A purchase order needs at least one line to be approved.");
        }

        Status = PurchaseOrderStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void MarkConverted()
    {
        if (Status != PurchaseOrderStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved purchase order can be converted to a Purchase Bill.");
        }

        Status = PurchaseOrderStatus.Converted;
    }

    /// <summary>
    /// Phase 58 -- records that a Goods Received Note was raised against this order. Allowed on an
    /// Approved order and on a Converted one (a bill first, the goods later, is ordinary), never twice:
    /// one order, one GRN, the same one-shot rule <see cref="MarkConverted"/> applies to the bill.
    /// </summary>
    public void MarkReceived()
    {
        if (Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.Converted))
        {
            throw new InvalidOperationException("Only an Approved purchase order can be received.");
        }

        if (IsReceived)
        {
            throw new InvalidOperationException("This purchase order has already been received on a Goods Received Note.");
        }

        ReceivedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Mirror of Quotation.Void -- a Converted purchase order (live dependent: the
    /// PurchaseBill created from it) is rejected by EnsureApproved's plain status check, and so,
    /// since phase 58, is a received one (live dependent: its Goods Received Note).</summary>
    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();

        if (IsReceived)
        {
            throw new InvalidOperationException("Only an Approved purchase order can be voided.");
        }

        Status = PurchaseOrderStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Phase 20b -- tenant-defined status pipeline (CustomStatus), orthogonal to the
    /// Draft/Approved/Void/Converted lifecycle above -- see Quotation.SetCustomStatus's identical
    /// doc comment (same live-confirmed reasoning).</summary>
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

        // Phase 39 -- sanitised here rather than in the handler, so that no future caller inside the
        // Domain can store markup that skipped the gate. RichText.Sanitize also collapses
        // "<p><br></p>" to null, which is what the editor emits for an empty field.
        Terms = RichText.Sanitize(terms);
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

    /// <summary>
    /// Phase 32 -- sets the billing location this document is raised from. Draft-only, exactly like
    /// <see cref="SetCurrency"/> beside it and for a related reason: an Approved document's location
    /// is what its numbering pool was drawn from (see DocumentNumberingRule.LocationWiseNumbering)
    /// and what every location-filtered report has already counted it under, so moving it afterwards
    /// would silently restate both.
    ///
    /// <para>Accepts null -- a tenant whose <see cref="Domain.Tenancy.LocationScopeMode"/> excludes
    /// this document type simply never sets one, which is why the column is nullable rather than
    /// defaulted to HeadOffice at the aggregate level. The Create handler resolves the default.</para>
    /// </summary>
    public void SetLocation(Guid? locationId)
    {
        EnsureDraft();
        LocationId = locationId;
    }

    /// <summary>Phase 44 -- fills in a billing location that was never set, for
    /// <c>BackfillDocumentLocationsCommand</c>. Allowed after Approve, unlike <see cref="SetLocation"/>,
    /// and refuses to move one that is already set. See <c>Sales.Invoice.BackfillLocation</c> for the
    /// full reasoning.</summary>
    public void BackfillLocation(Guid locationId)
    {
        if (LocationId is not null)
        {
            throw new InvalidOperationException(
                "This document already has a billing location; the backfill only fills in a missing one.");
        }

        LocationId = locationId;
    }

    private void EnsureDraft()
    {
        if (Status != PurchaseOrderStatus.Draft)
        {
            throw new InvalidOperationException("This purchase order is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != PurchaseOrderStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved purchase order can be voided.");
        }
    }
}
