using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;

namespace ErpApp.Domain.Sales;

/// <summary>
/// First real use of IGlPostingRule&lt;TDocument&gt; for a non-JournalVoucher/CashTransfer type,
/// and the first aggregate in this codebase with a required WarehouseId (architecture-spec.md
/// §3.5's "Warehouse is required specifically on Invoice and PurchaseBill" finding). Approve()
/// itself stays GL-ignorant, same split JournalVoucher established -- it only assigns the real
/// number and flips Status; the Application-layer ApproveInvoiceCommandHandler resolves each
/// line's Sales Account (Product.SalesAccountId, falling back to TenantSettings'
/// DefaultSalesAccountId) and calls IGlPostingRule&lt;InvoicePostingInput&gt; separately, since
/// that resolution needs DB reads Domain can't perform (see Application.Sales.Posting's doc
/// comments for the full reasoning).
///
/// ReferrerType/ReferrerId (architecture-spec.md §3.3) are set when this Invoice was created via
/// the Quotation-conversion flow -- null for a standalone Invoice.
///
/// Stock decrement is a deliberate no-op this phase (roadmap's own sequencing recommendation (a)):
/// Approve calls IStockAvailabilityPolicy, which is a literal always-Ok stub until Phase 7's real
/// FIFO ledger exists -- see Application.Sales.Stock.
///
/// <para><b>Export sales (FR-5.8, Phase 23).</b> IsExport + ExportCountry/ExportDeclarationNo/
/// ExportDeclarationDate mirror PurchaseBill's existing IsImport block, and like it the detail
/// fields are nullable regardless and only meaningful when the flag is set. Two differences from
/// that block, both live-confirmed against the reference product rather than assumed:</para>
/// <para>1. The detail fields are <b>optional even when the flag is set</b> -- the live form marks
/// Customer/Date/Due Date/Warehouse with a required asterisk and pointedly does not mark Country,
/// Date or Document No. PurchaseBill's import fields are required-when-flagged; this is not.</para>
/// <para>2. <b>An export sale is zero-rated, and the aggregate enforces it.</b> On the live form,
/// ticking "This is export sales" disables the per-line Tax selector outright and pins every line
/// to "0 Vat" (verified in the DOM: the control carries ant-select-disabled). So SetExport and
/// AddLine both coerce every line's VatRate to ZeroVat -- putting the rule in the aggregate rather
/// than in a validator or the Angular form, because it is an invariant of the document and not a
/// property of one entry path. Note ZeroVat (zero-rated) is deliberately not NoVat (exempt): both
/// compute 0 VAT, but they are different statutory buckets and VAT Summary reports them separately.
/// </para>
/// </summary>
public sealed class Invoice
{
    public const string DraftCode = "DRAFT";

    /// <summary>Phase 61 -- a till sale's money is whole paisa: every line figure, the round-off, each
    /// tender and the change. See <see cref="InvoiceLine.CreatePos"/> for why an ERP line is not.</summary>
    public const int PosMoneyScale = 2;

    private readonly List<InvoiceLine> _lines = [];
    private readonly List<InvoiceTender> _tenders = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }

    /// <summary>
    /// Phase 31 -- a <b>stored</b> due date, closing phase-26b's carried item and phase-30's
    /// <c>$[DUE_DATE]$</c>.
    ///
    /// <para>Confirmed live 2026-09-06: the reference product's Invoice form carries its own
    /// required Due Date input, defaulted to the invoice date and freely editable, and it carries no
    /// Credit Terms field at all. Its Invoice Age report shows due dates that diverge from the
    /// document date by intervals no configured credit term could produce (03-06-2026 →
    /// 27-08-2026), which is the proof that this is a stored field rather than a derivation from
    /// the contact's term. The term is a <i>prefill source</i> only -- the client seeds this from
    /// the customer's CreditTerm.DueDays when one is picked, and never again.</para>
    ///
    /// <para>Non-nullable, defaulting to <see cref="Date"/>, so every ageing consumer can read it
    /// unconditionally and the backfill on existing rows is exactly "= Date" -- which is also the
    /// answer phase-9 and phase-26b were already improvising when they aged an invoice from its own
    /// document date.</para>
    /// </summary>
    public DateOnly DueDate { get; private set; }
    public string? Reference { get; private set; }
    public bool IsExport { get; private set; }
    public string? ExportCountry { get; private set; }
    public string? ExportDeclarationNo { get; private set; }
    public DateOnly? ExportDeclarationDate { get; private set; }
    public InvoiceStatus Status { get; private set; }
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
    public DocumentType? ReferrerType { get; private set; }
    public Guid? ReferrerId { get; private set; }
    public decimal DiscountPct { get; private set; }

    /// <summary>Phase 27b -- the "+ Add Terms and Conditions" block's stored text (FR-11.3's
    /// CustomTemplate finding its first consumer). Free text on the document, <b>not</b> a pointer
    /// to the CustomTemplate it was seeded from: the reference product pre-fills the editor from a
    /// chosen template and then lets the user edit it freely (confirm-live 2026-09-03), so the
    /// template is a starting point, and a document must keep the words it was actually issued with
    /// even after that template is edited or deleted.</summary>
    public string? Terms { get; private set; }

    /// <summary>Phase 61 -- which front end raised this invoice. See <see cref="SalesChannel"/>.</summary>
    public SalesChannel Channel { get; private set; }

    /// <summary>Phase 61 -- the till session a POS sale was rung up in; null on every ERP invoice.
    /// It is what the session reader selects by, and what decides whether a POS sale may still be
    /// voided (only while its session is open).</summary>
    public Guid? PosSessionId { get; private set; }

    /// <summary>Phase 61 -- the till tab the sale was rung up on (Retail, Dine In, Take Away,
    /// Delivery). The vendor's <c>order_type</c> takes exactly the tab's values, so this reuses
    /// <see cref="PosTab"/> rather than inventing a parallel enum that would have to be mapped onto
    /// it by name. Null on every ERP invoice.</summary>
    public PosTab? OrderType { get; private set; }

    /// <summary>
    /// Phase 61 -- the amount added to (positive) or taken off (negative) the bill to bring it to a
    /// whole rupee, when the location rounds. <b>Outside the VAT base</b>: the vendor's own bill rounds
    /// 632.80 to 633 with VAT unchanged at 72.80, and a rounding is not consideration for a supply.
    /// It is part of what the customer owes, so <see cref="GrandTotal"/> includes it and so does the
    /// receivable.
    /// </summary>
    public decimal RoundOff { get; private set; }

    /// <summary>
    /// Phase 61 -- cash handed back to the customer. Always cash and always out of the session's
    /// drawer (Decision C of docs/phase-61-status.md): change can only be given out of cash that was
    /// tendered, so it is capped by the cash tenders, and it never leaves anything on credit.
    /// </summary>
    public decimal ChangeAmount { get; private set; }

    /// <summary>
    /// Phase 62 -- the most a single abbreviated tax invoice may be for: VAT Rules 2053 Rule 18(6),
    /// raised from Rs 5,000 to Rs 10,000 by the 21st Amendment (Jestha 15, 2076). Inclusive: the rule
    /// refuses a transaction of <i>more than</i> the amount.
    /// </summary>
    public const decimal AbbreviatedTaxInvoiceLimit = 10_000m;

    /// <summary>
    /// Phase 62 -- this till sale was issued as an <b>abbreviated tax invoice</b> (VAT Rules Rule 18)
    /// rather than a full one (Rule 17). Stored, not derived at print time, because it is a fact about
    /// the bill that was handed over: a reprint must carry the same heading as the original even after
    /// the location's setting changes. Always false on an ERP invoice.
    /// </summary>
    public bool IsAbbreviatedTaxInvoice { get; private set; }

    /// <summary>
    /// Phase 65 -- the restaurant order this till sale bills (all or part of), or null. Every line of such
    /// an invoice names the order line it bills (<see cref="InvoiceLine.PosOrderLineId"/>); this header
    /// copy is what finds an order's bills, and what a void reads to give the order its quantities back.
    /// </summary>
    public Guid? PosOrderId { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    /// <summary>Phase 61 -- how a till sale was paid. Empty on every ERP invoice, which is settled
    /// afterwards by a Payment allocated against it.</summary>
    public IReadOnlyList<InvoiceTender> Tenders => _tenders;

    /// <summary>What the customer owes for this invoice: every line's amount, service charge and VAT,
    /// plus the round-off. For an ERP invoice the last two terms are zero, so this is the figure it
    /// always was.</summary>
    public decimal GrandTotal => _lines.Sum(x => x.LineTotal) + RoundOff;

    public decimal ServiceChargeTotal => _lines.Sum(x => x.ServiceChargeAmount);

    /// <summary>Everything handed over, before change.</summary>
    public decimal TenderedAmount => _tenders.Sum(x => x.Amount);

    /// <summary>What the tenders settled: handed over, less the change given back.</summary>
    public decimal SettledAmount => TenderedAmount - ChangeAmount;

    /// <summary>The unsettled remainder, left on the customer's account as a receivable -- what the
    /// vendor calls a Credit tender. Zero for a fully paid till sale; the whole total for an ERP
    /// invoice, which is settled later.</summary>
    public decimal CreditAmount => GrandTotal - SettledAmount;

    private Invoice()
    {
    }

    public static Invoice Create(
        Guid organizationId,
        Guid contactId,
        Guid warehouseId,
        DateOnly date,
        string? reference,
        DocumentType? referrerType,
        Guid? referrerId,
        DateOnly? dueDate = null,
        decimal discountPct = 0,
        bool isExport = false,
        string? exportCountry = null,
        string? exportDeclarationNo = null,
        DateOnly? exportDeclarationDate = null)
    {
        EnsureValidDiscountPct(discountPct);

        return new Invoice
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            WarehouseId = warehouseId,
            Code = DraftCode,
            Date = date,
            DueDate = dueDate ?? date,
            Reference = reference,
            IsExport = isExport,
            ExportCountry = isExport ? exportCountry : null,
            ExportDeclarationNo = isExport ? exportDeclarationNo : null,
            ExportDeclarationDate = isExport ? exportDeclarationDate : null,
            Status = InvoiceStatus.Draft,
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
        decimal discountPct,
        DateOnly? dueDate = null,
        bool isExport = false,
        string? exportCountry = null,
        string? exportDeclarationNo = null,
        DateOnly? exportDeclarationDate = null)
    {
        EnsureDraft();
        EnsureValidDiscountPct(discountPct);
        ContactId = contactId;
        WarehouseId = warehouseId;
        Date = date;
        DueDate = dueDate ?? date;
        Reference = reference;
        DiscountPct = discountPct;
        SetExport(isExport, exportCountry, exportDeclarationNo, exportDeclarationDate);
    }

    /// <summary>Turning the export flag on <b>re-rates every line already on the document</b> to
    /// ZeroVat, and turning it off leaves them alone -- the user picks the rate again. Rebuilding
    /// the lines is what keeps "an export invoice is zero-rated" true no matter which order the
    /// user ticks the box and adds lines in; AddLine covers the other order.</summary>
    public void SetExport(
        bool isExport, string? exportCountry, string? exportDeclarationNo, DateOnly? exportDeclarationDate)
    {
        EnsureDraft();

        // Phase 61 -- the rebuild below would re-create every line through the ERP factory and drop a
        // till line's service charge. A counter sale is not an export sale anyway.
        if (isExport && Channel == SalesChannel.Pos)
        {
            throw new InvalidOperationException("A till sale cannot be an export sale.");
        }

        IsExport = isExport;
        ExportCountry = isExport ? exportCountry : null;
        ExportDeclarationNo = isExport ? exportDeclarationNo : null;
        ExportDeclarationDate = isExport ? exportDeclarationDate : null;

        if (!isExport)
        {
            return;
        }

        var existing = _lines.ToList();
        _lines.Clear();
        foreach (var line in existing)
        {
            // Phase 51 -- line.BatchId rides through this rebuild. Flipping the export flag
            // re-creates every line to coerce its VAT rate, and a field left off this call is
            // silently dropped by an action that has nothing to do with it (phase 35a: adding a
            // field to many aggregates owes write, read, and every prefill between).
            //
            // Phase 52 -- line.UnitId and line.ConversionFactor ride through it for the same
            // reason, and the factor is the more dangerous of the two: dropping it would reset it
            // to one, so an export invoice for 2 CTN would quietly start relieving 2 pieces
            // instead of 24, with the money unchanged and nothing on screen to show for it.
            _lines.Add(InvoiceLine.Create(
                Id, line.ProductId, line.Quantity, line.Rate, VatRate.ZeroVat, line.DiscountPct, DiscountPct,
                line.BatchId, line.UnitId, line.ConversionFactor));
        }
    }

    /// <summary>
    /// Phase 61 -- a till sale (phase 59 Decision C). The same aggregate as an ERP invoice, created in
    /// Draft and approved in the same command by <c>CreatePosSaleCommandHandler</c>, so it is numbered
    /// at approve exactly like every other invoice (location-wise numbering included).
    ///
    /// <para>The location is set here and not through <see cref="SetLocation"/>'s "null means the
    /// default" path: a till is always somewhere, and Invoice carries a location in every
    /// <c>LocationScopeMode</c>. The due date is the sale date, because whatever is left on credit at
    /// a counter is due when the customer walks out.</para>
    /// </summary>
    public static Invoice CreatePosSale(
        Guid organizationId,
        Guid contactId,
        Guid warehouseId,
        DateOnly date,
        Guid locationId,
        Guid posSessionId,
        PosTab orderType,
        decimal discountPct = 0)
    {
        if (locationId == Guid.Empty || posSessionId == Guid.Empty)
        {
            throw new InvalidOperationException("A till sale names its location and its session.");
        }

        if (!Enum.IsDefined(orderType))
        {
            throw new InvalidOperationException($"'{orderType}' is not a till order type.");
        }

        var invoice = Create(organizationId, contactId, warehouseId, date, null, null, null, date, discountPct);
        invoice.Channel = SalesChannel.Pos;
        invoice.PosSessionId = posSessionId;
        invoice.OrderType = orderType;
        invoice.LocationId = locationId;
        return invoice;
    }

    /// <summary>Phase 61 -- adds a till line carrying its own service charge rate. See
    /// <see cref="InvoiceLine.CreatePos"/>.</summary>
    public void AddPosLine(
        Guid productId, decimal quantity, decimal rate, VatRate vatRate, decimal discountPct,
        Guid? unitId, decimal conversionFactor, Guid? batchId, decimal serviceChargeRate)
    {
        EnsureDraft();
        EnsurePos();
        EnsureUnsettled();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("An invoice line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        _lines.Add(InvoiceLine.CreatePos(
            Id, productId, quantity, rate, vatRate, discountPct, DiscountPct, batchId, unitId,
            conversionFactor, serviceChargeRate));

        // A line added after a rounding would leave the bill rounded to the wrong rupee.
        RoundOff = 0m;
    }

    /// <summary>
    /// Phase 65 -- makes this till sale a bill for (part of) a restaurant order. Set before any line, so
    /// an invoice is either all order lines or none.
    /// </summary>
    public void BillPosOrder(Guid posOrderId)
    {
        EnsureDraft();
        EnsurePos();

        if (posOrderId == Guid.Empty || _lines.Count > 0 || PosOrderId is not null)
        {
            throw new InvalidOperationException("A bill names its order once, before its first line.");
        }

        PosOrderId = posOrderId;
    }

    /// <summary>Phase 65 -- adds a line billing part of an order line, priced by the order's bill planner.
    /// See <see cref="InvoiceLine.CreatePosOrderPart"/>.</summary>
    public void AddPosOrderLine(
        Guid posOrderLineId, Guid productId, decimal quantity, decimal rate, VatRate vatRate, Guid? unitId,
        decimal conversionFactor, decimal serviceChargeRate, PosLineArithmetic.Figures figures)
    {
        EnsureDraft();
        EnsurePos();
        EnsureUnsettled();

        if (PosOrderId is null)
        {
            throw new InvalidOperationException("Only a bill for an order carries order lines.");
        }

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("An invoice line needs a positive Quantity and a non-negative Rate.");
        }

        if (DiscountPct != 0m)
        {
            throw new InvalidOperationException("A bill for an order carries no discount.");
        }

        _lines.Add(InvoiceLine.CreatePosOrderPart(
            Id, posOrderLineId, productId, quantity, rate, vatRate, unitId, conversionFactor, serviceChargeRate,
            figures));

        RoundOff = 0m;
    }

    /// <summary>
    /// Phase 65 -- a round-off the caller decided, for a bill whose rounding depends on other documents:
    /// an order paid in parts rounds its running total, so each part's round-off is set by the order's
    /// bill planner (<c>PosOrderBill</c>), never by this bill alone. Whole paisa, under a rupee either
    /// way, and never below zero.
    /// </summary>
    public void SetPosRoundOff(decimal roundOff)
    {
        EnsureDraft();
        EnsurePos();
        EnsureUnsettled();

        if (decimal.Round(roundOff, PosMoneyScale) != roundOff || Math.Abs(roundOff) >= 1m)
        {
            throw new InvalidOperationException("A round-off is whole paisa and under a rupee either way.");
        }

        if (_lines.Sum(x => x.LineTotal) + roundOff < 0m)
        {
            throw new InvalidOperationException("A round-off cannot take a bill below zero.");
        }

        RoundOff = roundOff;
    }

    /// <summary>
    /// Phase 61 -- rounds what the customer owes to the nearest whole rupee (half away from zero, so
    /// 632.50 becomes 633 and 632.49 becomes 632), when the location rounds. Phase 60 modelled the
    /// vendor's <c>round_amount</c> as a flag because the one behaviour observed was 632.80 to 633;
    /// this is that rule and no other increment.
    /// </summary>
    public void ApplyRoundOff()
    {
        EnsureDraft();
        EnsurePos();
        EnsureUnsettled();

        var unrounded = _lines.Sum(x => x.LineTotal);
        RoundOff = decimal.Round(unrounded, 0, MidpointRounding.AwayFromZero) - unrounded;
    }

    /// <summary>
    /// Phase 61 -- records how a till sale was paid. Called once, after the lines and the round-off.
    ///
    /// <para>The invariants are about the drawer and the receivable, in that order:</para>
    /// <list type="bullet">
    /// <item>change comes out of cash that was handed over, so it cannot exceed the cash tenders --
    /// a card is not over-swiped to give cash back;</item>
    /// <item>nothing may be settled beyond what is owed: tenders less change are at most the total;</item>
    /// <item>change is only given on a bill paid in full -- handing cash back while leaving the rest on
    /// credit would be lending the customer their own change.</item>
    /// </list>
    /// <para>What is left unsettled is <see cref="CreditAmount"/>. Whether this customer may carry it
    /// (the walk-in may not; a named customer is subject to phase 31's credit control) is the
    /// handler's decision, because it needs the contact row and the tenant's policy.</para>
    /// </summary>
    public void Settle(IReadOnlyCollection<TenderInput> tenders, decimal changeAmount)
    {
        EnsureDraft();
        EnsurePos();
        EnsureUnsettled();

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("A till sale needs at least one line before it is paid.");
        }

        if (changeAmount < 0m || decimal.Round(changeAmount, PosMoneyScale) != changeAmount)
        {
            throw new InvalidOperationException("Change must be zero or a positive amount in whole paisa.");
        }

        // Built aside and checked whole before anything is kept, so a refused settlement leaves the
        // sale exactly as unpaid as it was.
        var built = tenders
            .Select(x => InvoiceTender.Create(Id, x.PaymentModeId, x.Kind, x.AccountId, x.Amount))
            .ToList();

        var cashTendered = built.Where(x => x.Kind == PaymentModeKind.Cash).Sum(x => x.Amount);
        if (changeAmount > cashTendered)
        {
            throw new InvalidOperationException(
                $"Change ({changeAmount:0.00}) can only come out of cash handed over ({cashTendered:0.00}).");
        }

        var settled = built.Sum(x => x.Amount) - changeAmount;
        if (settled > GrandTotal)
        {
            throw new InvalidOperationException(
                $"The tenders less change come to {settled - GrandTotal:0.00} more than the bill. Give that much "
                + "more change, or take less in a non-cash mode.");
        }

        if (changeAmount > 0m && settled != GrandTotal)
        {
            throw new InvalidOperationException(
                "Change is only given on a bill paid in full; the rest of this one would be left on credit.");
        }

        _tenders.AddRange(built);
        ChangeAmount = changeAmount;
        _settled = true;
    }

    // Not persisted: it only has to stop a second Settle while the sale is still the in-memory draft
    // its command is building. Once approved, EnsureDraft refuses everything this guards.
    private bool _settled;

    /// <summary>
    /// Phase 62 -- issues this till sale as an abbreviated tax invoice. Only once it is paid, because
    /// the limit is on what the bill comes to and nothing can be added after <see cref="Settle"/>.
    /// Whether the seller may issue one at all (VAT registration, the Tax Officer's permission, a
    /// buyer who did not ask for a full invoice) is the handler's question; the one fact the bill
    /// itself can answer, its amount, is refused here.
    /// </summary>
    public void IssueAsAbbreviatedTaxInvoice()
    {
        EnsureDraft();
        EnsurePos();

        if (!_settled)
        {
            throw new InvalidOperationException("A bill is issued as abbreviated only once it is paid.");
        }

        if (GrandTotal > AbbreviatedTaxInvoiceLimit)
        {
            throw new InvalidOperationException(
                $"An abbreviated tax invoice may be for at most {AbbreviatedTaxInvoiceLimit:0.00} (VAT Rules, Rule 18(6)); "
                + $"this bill is {GrandTotal:0.00}.");
        }

        IsAbbreviatedTaxInvoice = true;
    }

    /// <summary>What <see cref="Settle"/> needs of one tender, already resolved from its payment mode.</summary>
    public sealed record TenderInput(Guid PaymentModeId, PaymentModeKind Kind, Guid AccountId, decimal Amount);

    public void AddLine(
        Guid productId, decimal quantity, decimal rate, VatRate vatRate, decimal discountPct,
        Guid? unitId, decimal conversionFactor, Guid? batchId = null)
    {
        EnsureDraft();

        if (Channel == SalesChannel.Pos)
        {
            throw new InvalidOperationException("A till sale's lines carry a service charge rate; add them with AddPosLine.");
        }

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("An invoice line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        // An export sale is zero-rated: the live reference product disables the line's Tax selector
        // entirely when the flag is set, so a caller's choice here is not merely overridden, it was
        // never offered. Enforced in the aggregate so no entry path can bypass it.
        var effectiveVatRate = IsExport ? VatRate.ZeroVat : vatRate;

        _lines.Add(InvoiceLine.Create(
            Id, productId, quantity, rate, effectiveVatRate, discountPct, DiscountPct, batchId, unitId,
            conversionFactor));
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
            throw new InvalidOperationException("An invoice needs at least one line to be approved.");
        }

        Status = InvoiceStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = InvoiceStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
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

    /// <summary>
    /// Phase 44 -- fills in a billing location that was never set, for the one-off backfill an Admin
    /// runs after widening <see cref="Domain.Tenancy.LocationScopeMode"/>
    /// (<c>BackfillDocumentLocationsCommand</c>).
    ///
    /// <para><b>Why this is allowed after Approve when <see cref="SetLocation"/> is not.</b> The two
    /// reasons that one is draft-only are exactly the two that cannot apply here. A document with no
    /// location was numbered from the <i>unscoped</i> pool, so there is no location-wise numbering to
    /// restate. And it was counted under no location at all, so no filtered report's past answer is
    /// revised -- it was missing from every one of them, which is the defect this repairs.</para>
    ///
    /// <para><b>It refuses to move a location that is already set.</b> That is the case
    /// <see cref="SetLocation"/> guards, and a guard is not weakened by adding a method that cannot
    /// reach past it.</para>
    /// </summary>
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
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException("This invoice is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != InvoiceStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved invoice can be voided.");
        }
    }

    private void EnsurePos()
    {
        if (Channel != SalesChannel.Pos)
        {
            throw new InvalidOperationException("Only a till sale carries service charge, round-off and tenders.");
        }
    }

    private void EnsureUnsettled()
    {
        if (_settled || _tenders.Count > 0)
        {
            throw new InvalidOperationException("This till sale has already been paid.");
        }
    }
}
