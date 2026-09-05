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

    /// <summary>
    /// Phase 29 (FR-6.15). The scale every per-line additional-cost allocation is rounded to --
    /// the same (18,4) the Amount columns themselves carry, so an allocation can always be stored
    /// exactly as computed. The last line in a row's scope takes the remainder rather than its own
    /// rounded share, which makes <c>sum(allocations) == row.Amount</c> true to the paisa for every
    /// row, leaving the unit-cost rounding downstream as the phase's only residue.
    /// </summary>
    public const int AllocationScale = 4;

    private readonly List<PurchaseBillLine> _lines = [];
    private readonly List<PurchaseBillAdditionalCost> _additionalCosts = [];

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

    /// <summary>
    /// Phase 29 (FR-6.15). Display shape of the live "Add product-wise" checkbox, which swaps the
    /// Additional Cost section between a list of allocation rules and a product-by-cost-term matrix
    /// of hand-typed cells. It changes nothing about the arithmetic -- a typed cell is just a row
    /// that already names its product -- so it is one bool here rather than a second entity, kept
    /// only so reopening a bill re-renders the section the way it was filled in.
    /// </summary>
    public bool IsProductWiseAdditionalCost { get; private set; }

    /// <summary>
    /// Phase 29, both written at Approve by <see cref="RecordAdditionalCostCapitalisation"/> and
    /// both in <b>base</b> currency (everything else on this aggregate is in
    /// <see cref="CurrencyCode"/>). <c>CapitalisedAdditionalCost</c> is the extra value the FIFO
    /// layers actually received; <c>AdditionalCostRoundingAdjustment</c> is the part of the entered
    /// additional cost that unit-cost rounding would not let them receive. The second is the
    /// phase's named residue, in the ProductionJournal.CostRoundingAdjustment tradition: bounded by
    /// half of the last unit-cost decimal per unit received, disclosed rather than absorbed.
    /// </summary>
    public decimal? CapitalisedAdditionalCost { get; private set; }

    /// <inheritdoc cref="CapitalisedAdditionalCost"/>
    public decimal? AdditionalCostRoundingAdjustment { get; private set; }

    public IReadOnlyList<PurchaseBillLine> Lines => _lines;
    public IReadOnlyList<PurchaseBillAdditionalCost> AdditionalCosts => _additionalCosts;

    public decimal GrandTotal => _lines.Sum(x => x.Amount + x.VatAmount);

    /// <summary>
    /// The Additional Cost section's own total, in the document's currency. Deliberately <b>not</b>
    /// part of <see cref="GrandTotal"/>: confirmed live 2026-09-04 that the reference product
    /// excludes it from Sub Total and Grand Total alike, and credits the supplier only the goods
    /// total, which is what makes this a cost to capitalise rather than a bigger payable.
    /// </summary>
    public decimal AdditionalCostTotal => _additionalCosts.Sum(x => x.Amount);

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

    /// <summary>Phase 29 (FR-6.15). Adds one Additional Cost row. <paramref name="productId"/> null
    /// is the live picker's "All Product".</summary>
    public void AddAdditionalCost(Guid costTermId, Guid? productId, AdditionalCostMethod method, decimal amount)
    {
        EnsureDraft();
        _additionalCosts.Add(PurchaseBillAdditionalCost.Create(Id, costTermId, productId, method, amount));
    }

    public void ClearAdditionalCosts()
    {
        EnsureDraft();
        _additionalCosts.Clear();
    }

    /// <inheritdoc cref="IsProductWiseAdditionalCost"/>
    public void SetProductWiseAdditionalCost(bool isProductWise)
    {
        EnsureDraft();
        IsProductWiseAdditionalCost = isProductWise;
    }

    /// <summary>
    /// Phase 29 (FR-6.15). Spreads every Additional Cost row across the bill's <b>goods</b> lines
    /// and records the result, one <see cref="PurchaseBillAdditionalCostAllocation"/> per (row,
    /// line). Pure arithmetic in the document's own currency -- the caller passes in which products
    /// are Goods, because that is a database fact, exactly as ApproveProductionJournalCommandHandler
    /// hands ProductionJournal the FIFO costs before its roll-up runs.
    ///
    /// <para><b>Goods only, and this is a deliberate divergence.</b> The reference product's Product
    /// picker offers service lines too (confirmed live 2026-09-04 by putting a Service line on a
    /// draft and finding it in the list). It can afford to: that tenant is periodic, so its
    /// additional cost posts no journal at all and lives only in a stock-costing subsystem a service
    /// line simply never reaches. Here the whole purpose is to capitalise the cost into a FIFO
    /// layer, and a service line creates no layer -- so a cost allocated to one would have nowhere
    /// to go and would vanish, breaking the conservation law this phase exists to hold. A row that
    /// names a service product is therefore rejected outright rather than silently dropped, and
    /// "All Product" means all <i>goods</i> lines.</para>
    ///
    /// <para>Each row's own Amount is conserved exactly: shares are rounded to
    /// <see cref="AllocationScale"/> and the last line in scope takes the remainder.</para>
    ///
    /// <para><b>Returns the allocations it created</b>, rather than leaving the caller to walk the
    /// graph for them. These rows are appended to <see cref="PurchaseBillAdditionalCost"/> instances
    /// that EF is already tracking, and a child appended to an already-tracked parent's encapsulated
    /// collection is detected as <i>Modified</i>, not <i>Added</i> -- phase-24 bug #1, whose symptom
    /// is a <c>DbUpdateConcurrencyException</c> ("attempted to update or delete an entity that does
    /// not exist in the store") from a handler that looks correct. The documented remedy is exactly
    /// this: have the Domain method report the change so the handler can <c>AddRange</c> it through
    /// the child DbSet.</para>
    /// </summary>
    public IReadOnlyList<PurchaseBillAdditionalCostAllocation> AllocateAdditionalCosts(IReadOnlySet<Guid> goodsProductIds)
    {
        var goodsLines = _lines.Where(x => goodsProductIds.Contains(x.ProductId)).ToList();
        var created = new List<PurchaseBillAdditionalCostAllocation>();

        foreach (var cost in _additionalCosts)
        {
            var scope = cost.ProductId is { } productId
                ? goodsLines.Where(x => x.ProductId == productId).ToList()
                : goodsLines;

            if (scope.Count == 0)
            {
                throw new InvalidOperationException(
                    cost.ProductId is null
                        ? "An additional cost cannot be allocated: this purchase bill has no goods lines to carry it."
                        : "An additional cost names a product that is not a goods line on this purchase bill.");
            }

            var basis = scope
                .Select(x => cost.Method == AdditionalCostMethod.Quantity ? x.Quantity : x.Amount)
                .ToList();
            var totalBasis = basis.Sum();

            if (totalBasis <= 0)
            {
                throw new InvalidOperationException(
                    "An additional cost cannot be allocated: the goods lines it applies to total zero on its chosen Method.");
            }

            var allocated = 0m;
            for (var i = 0; i < scope.Count; i++)
            {
                var share = i == scope.Count - 1
                    ? cost.Amount - allocated
                    : Math.Round(cost.Amount * basis[i] / totalBasis, AllocationScale, MidpointRounding.AwayFromZero);

                created.Add(cost.Allocate(scope[i].Id, share));
                allocated += share;
            }
        }

        return created;
    }

    /// <inheritdoc cref="CapitalisedAdditionalCost"/>
    public void RecordAdditionalCostCapitalisation(decimal capitalisedAdditionalCost, decimal roundingAdjustment)
    {
        CapitalisedAdditionalCost = capitalisedAdditionalCost;
        AdditionalCostRoundingAdjustment = roundingAdjustment;
    }

    /// <summary>The additional cost allocated to one line, in the document's currency -- the sum of
    /// every row's share of it. Zero before <see cref="AllocateAdditionalCosts"/> has run.</summary>
    public decimal AllocatedAdditionalCostFor(Guid purchaseBillLineId) =>
        _additionalCosts
            .SelectMany(x => x.Allocations)
            .Where(x => x.PurchaseBillLineId == purchaseBillLineId)
            .Sum(x => x.Amount);

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
