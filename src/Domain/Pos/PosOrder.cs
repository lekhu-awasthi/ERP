using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.Pos;

public enum PosOrderStatus
{
    Open = 1,
    Voided = 2,

    /// <summary>Phase 65 -- everything the guests are having has been billed, so the table is free. A
    /// paid order may still have food to come (a Take Away is paid before it is cooked), so the kitchen
    /// can still serve it; nothing else changes it, unless voiding one of its invoices reopens it.</summary>
    Settled = 3,
}

/// <summary>A line a send adds to an order, priced and routed by the caller from the catalogue.</summary>
public sealed record PosOrderNewLine(
    Guid ProductId,
    Guid? UnitId,
    decimal ConversionFactor,
    decimal Quantity,
    decimal Rate,
    VatRate VatRate,
    decimal ServiceChargeRate,
    PackagingFee PackagingFee,
    string? Note,
    Guid? KitchenStationId);

/// <summary>A quantity against one existing line: more of it (a send), served, or discarded.</summary>
public sealed record PosOrderLineQuantity(Guid LineId, decimal Quantity);

/// <summary>What a send created, for the caller to add through its own sets (phase 24).</summary>
public sealed record PosOrderSendResult(IReadOnlyList<PosOrderLine> NewLines, IReadOnlyList<KitchenTicket> NewTickets);

/// <summary>Phase 68 -- what a take-away mark or a transfer created on one order, for the caller to add
/// through its own sets (phase 24).</summary>
public sealed record PosOrderMoveResult(IReadOnlyList<PosOrderLine> NewLines, IReadOnlyList<KitchenTicket> NewTickets);

/// <summary>Phase 68 -- both halves of a transfer, and whether it emptied (and so voided) the source.</summary>
public sealed record PosOrderTransferResult(PosOrderMoveResult Source, PosOrderMoveResult Target, bool SourceEmptied);

/// <summary>A line's quantities, every one but <see cref="Served"/> a sum over its kitchen ticket lines.
/// Phase 68: <see cref="MovedIn"/> and <see cref="MovedOut"/> are what a take-away mark or a transfer
/// moved onto or off the line; they are neither ordered nor discarded.</summary>
public sealed record PosOrderLineQuantities(
    decimal Ordered, decimal Discarded, decimal Net, decimal Served, decimal MovedIn = 0m, decimal MovedOut = 0m)
{
    /// <summary>What the kitchen still owes the table.</summary>
    public decimal Outstanding => Net - Served;
}

/// <summary>
/// Phase 68 -- what a kitchen ticket is. Phase 64 told a send from a cancellation by its signs; a move
/// has both signs on one ticket (a take-away mark) or tells the kitchen of food leaving one table for
/// another, so the kind is stored.
/// </summary>
public enum KitchenTicketKind
{
    /// <summary>Food to cook: every line positive.</summary>
    Send = 1,

    /// <summary>A discard or a void: every line negative, with the reason.</summary>
    Cancellation = 2,

    /// <summary>Part of a dine-in line parcelled: minus on the dine-in line, plus on its take-away line.</summary>
    TakeAway = 3,

    /// <summary>Food moved to or from another order: one sign per ticket, naming the other order.</summary>
    Transfer = 4,
}

/// <summary>
/// Phase 65 -- one send ticket's line on the kitchen board: what it asked for, and how much of that has
/// since been served, cancelled, or is still to cook. Derived, never stored (see
/// <see cref="PosOrder.TicketProgress"/>).
/// </summary>
public sealed record KitchenTicketLineProgress(
    Guid OrderLineId, decimal Sent, decimal Served, decimal Cancelled, decimal Moved = 0m)
{
    public decimal Pending => Sent - Served - Cancelled - Moved;
}

public enum KitchenTicketState
{
    /// <summary>A send with something still to cook.</summary>
    Pending = 1,

    /// <summary>A send whose every item was served (or served in part and the rest cancelled).</summary>
    Served = 2,

    /// <summary>A send all of which was cancelled before any of it was served.</summary>
    Cancelled = 3,

    /// <summary>A cancellation ticket itself: the negative lines and the reason.</summary>
    Cancellation = 4,

    /// <summary>Phase 68 -- a transfer's outgoing ticket: food that left for another table.</summary>
    Moved = 5,
}

public sealed record KitchenTicketProgress(
    Guid TicketId, KitchenTicketState State, IReadOnlyList<KitchenTicketLineProgress> Lines);

/// <summary>
/// Phase 64 -- a restaurant's open order: a table's tab, a parcel being packed, a delivery being
/// cooked (phase 59 Decision E).
///
/// <para><b>Its own aggregate, not a Sales Order.</b> The vendor saves every dine-in order as a Sales
/// Order that is Approved and numbered at save (SO0002/1002/83-84, phase 59), so each table's tab takes
/// a ledger number, and its split bill rewrites that order's lines (defect 2). A Sales Order here is a
/// commercial commitment with a Draft/Approve lifecycle; an open tab is edited for hours and posts
/// nothing. So this aggregate carries no approval, takes a number from the tenant's own counter
/// (<c>ORD0001</c>, like a session's <c>SES0001</c>) and never from a document-numbering pool, reserves
/// no stock and posts nothing. Settling it into Invoices is phase 65's.</para>
///
/// <para><b>The kitchen ticket is the movement; the order line is the product.</b> Every quantity
/// change reaches the kitchen as a <see cref="KitchenTicket"/> line carrying the signed change -- a send
/// adds, a discard subtracts -- and the line stores no quantity of its own. Ordered, discarded and the
/// net quantity a guest pays for are sums over the ticket lines naming the line, so they cannot drift
/// from what the kitchen was told (phase 51's rule: a GROUP BY over the one quantity cannot disagree
/// with it; phase 55's: store the signed value). <see cref="PosOrderLine.ServedQuantity"/> is the one
/// counter stored, because serving is recorded nowhere else. The vendor keeps five counters per line
/// (<c>quantity, served, discarded, takeaway, transferred</c>) beside its tickets.
/// See docs/phase-64-status.md Decision B.</para>
///
/// <para><b>One open order per table</b>, by a filtered unique index rather than a read-then-write
/// check, so two waiters seating one table at once cannot both win. That index is also what makes a
/// table's occupancy derivable instead of stored.</para>
///
/// <para>Every change touches <see cref="LastActivityAt"/>, so with <see cref="RowVersion"/> two
/// changes to one order are serial: a discard checks the quantity it cancels against the sends it can
/// see, and a concurrent send must not slip between the check and the write.</para>
/// </summary>
public sealed class PosOrder
{
    public const string CodePrefix = "ORD";
    public const int MaxCovers = 100;
    public const int MaxLines = 200;
    public const int MaxNoteLength = 200;
    public const int MaxReasonLength = 200;

    /// <summary>Phase 72 -- the decimal places a discount percentage may have.</summary>
    public const int MaxDiscountScale = 2;

    private readonly List<PosOrderLine> _lines = [];
    private readonly List<KitchenTicket> _tickets = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = null!;
    public Guid BillingLocationId { get; private set; }

    /// <summary>Dine In, Take Away or Delivery: the Restaurant till's three tabs. A Retail hold stays in
    /// the browser (phase-62-status.md Decision C), so Retail is never an order type here.</summary>
    public PosTab OrderType { get; private set; }

    /// <summary>The table a Dine In order is seated at; null for Take Away and Delivery.</summary>
    public Guid? PosTableId { get; private set; }

    /// <summary>The vendor's <c>customer_count</c>: guests at the table.</summary>
    public int Covers { get; private set; }

    /// <summary>Null is the walk-in. A Delivery names its customer, because it goes to their address.</summary>
    public Guid? ContactId { get; private set; }

    public PosOrderStatus Status { get; private set; }

    /// <summary>The Nepal date the order was opened on: what the ERP list filters by.</summary>
    public DateOnly Date { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public string? VoidReason { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }

    /// <summary>Phase 65 -- when the last of the order was billed; null while open, and again if a void
    /// of one of its invoices reopens it.</summary>
    public DateTimeOffset? SettledAt { get; private set; }

    /// <summary>
    /// Phase 72 -- a discount on the whole order: a percentage off every line after the line's own, which
    /// is the bill's header discount in the till's one arithmetic (<see cref="PosLineArithmetic"/>). A line
    /// sent later takes it too. Set while nothing on the order is billed and fixed from the first bill, so
    /// every part is priced at the same terms and the bills still add up to the order (phase 65).
    /// </summary>
    public decimal DiscountPct { get; private set; }

    /// <summary>
    /// Phase 74 -- the order's <b>Amount</b> discount, rupees off the whole tab (the vendor's <i>Options &gt;
    /// Discount</i> with <i>Amount</i> chosen), never beside a <see cref="DiscountPct"/>. It is spread over the
    /// dishes in proportion to what each comes to, live until the first bill, and then each line's share is
    /// frozen (<see cref="PosOrderLine.OrderDiscountShare"/>): every part of a dish takes its quantity's share of
    /// that dish's, and the last part what is left, so the bills add up to the order and no one guest's bill takes
    /// the whole amount (the vendor's first split bill does, phase-74-status.md Decision B).
    /// </summary>
    public decimal DiscountAmount { get; private set; }

    /// <summary>
    /// Phase 72 -- the guest declined the service charge, which they may: a mandatory one has been void
    /// since the Supreme Court's ruling of 2023-01-25 (phase-61-status.md Decision A). Every line is then
    /// billed without it, while each keeps its own frozen rate. The tab's decision, set and fixed like
    /// <see cref="DiscountPct"/>.
    /// </summary>
    public bool ServiceChargeWaived { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<PosOrderLine> Lines => _lines;
    public IReadOnlyList<KitchenTicket> Tickets => _tickets;

    private PosOrder()
    {
    }

    public static PosOrder Open(
        Guid organizationId,
        Guid billingLocationId,
        string code,
        PosTab orderType,
        Guid? tableId,
        int covers,
        Guid? contactId,
        Guid userId,
        DateTimeOffset now)
    {
        if (billingLocationId == Guid.Empty || userId == Guid.Empty)
        {
            throw new InvalidOperationException("An order names its location and who opened it.");
        }

        if (orderType is not (PosTab.DineIn or PosTab.TakeAway or PosTab.Delivery))
        {
            throw new InvalidOperationException(
                $"'{orderType}' is not a restaurant order type. An order is Dine In, Take Away or Delivery.");
        }

        if (orderType == PosTab.DineIn && tableId is null)
        {
            throw new InvalidOperationException("A Dine In order is seated at a table.");
        }

        if (orderType != PosTab.DineIn && tableId is not null)
        {
            throw new InvalidOperationException($"A {Describe(orderType)} order has no table.");
        }

        if (orderType == PosTab.Delivery && contactId is null)
        {
            throw new InvalidOperationException("A Delivery order names the customer it goes to.");
        }

        EnsureCovers(orderType, covers);

        return new PosOrder
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = code,
            BillingLocationId = billingLocationId,
            OrderType = orderType,
            PosTableId = tableId,
            Covers = covers,
            ContactId = contactId,
            Status = PosOrderStatus.Open,
            Date = NepalTime.LocalDate(now),
            CreatedAt = now,
            CreatedByUserId = userId,
            LastActivityAt = now,
        };
    }

    /// <summary>
    /// Sends new lines, and more of existing ones, to the kitchen: one ticket per station for this
    /// send, each carrying only what changed (the vendor's delta KOT, read in phase 59: a third
    /// ticket carrying only Coke × 1). An increase goes to the station its line was first sent to,
    /// so a product re-routed since does not split one line across two kitchens.
    /// </summary>
    public PosOrderSendResult Send(
        IReadOnlyList<PosOrderNewLine> newLines,
        IReadOnlyList<PosOrderLineQuantity> increases,
        Guid userId)
    {
        EnsureOpen();

        if (newLines.Count == 0 && increases.Count == 0)
        {
            throw new InvalidOperationException("Nothing to send: add an item first.");
        }

        if (_lines.Count + newLines.Count > MaxLines)
        {
            throw new InvalidOperationException($"An order holds at most {MaxLines} lines.");
        }

        EnsureDistinct(increases);

        var changes = new List<(PosOrderLine Line, decimal Quantity)>();

        foreach (var increase in increases)
        {
            changes.Add((FindLine(increase.LineId), RequireQuantity(increase.Quantity)));
        }

        var created = new List<PosOrderLine>();

        foreach (var row in newLines)
        {
            var line = PosOrderLine.Create(Id, _lines.Count + created.Count + 1, row);
            created.Add(line);
            changes.Add((line, RequireQuantity(row.Quantity)));
        }

        _lines.AddRange(created);

        var tickets = Ticket(changes, userId, reason: null, KitchenTicketKind.Send);
        return new PosOrderSendResult(created, tickets);
    }

    /// <summary>Marks quantities as having left the kitchen for the table, partially if need be (the
    /// vendor's <c>mark-as-served</c> takes a quantity per item). Never more than is outstanding.</summary>
    public void Serve(IReadOnlyList<PosOrderLineQuantity> items)
    {
        // Phase 65 -- a settled order may still be cooking: a Take Away is paid before it is packed.
        EnsureNotVoided();
        EnsureNotEmpty(items, "Choose what was served.");
        EnsureDistinct(items);

        foreach (var item in items)
        {
            var line = FindLine(item.LineId);
            var quantity = RequireQuantity(item.Quantity);
            var outstanding = QuantitiesOf(line).Outstanding;

            if (quantity > outstanding)
            {
                throw new InvalidOperationException(
                    $"Line {line.LineNo} has {outstanding:0.####} still to serve, not {quantity:0.####}.");
            }

            line.AddServed(quantity);
        }

        Touch();
    }

    /// <summary>
    /// Takes quantities off the order after they reached the kitchen, with a reason, and tells the
    /// kitchen: a cancellation ticket per station carrying the negative change and the reason (the
    /// vendor's discard dialog requires a reason, and its ticket shows a negative line).
    ///
    /// <para>A discard may take back food already served -- a dish sent back -- as the vendor's does.
    /// It cancels what is unserved first; only beyond that does the served count come down, so served
    /// never exceeds what the guest is having.</para>
    ///
    /// <para>Phase 65 -- never what is already billed: <paramref name="invoiced"/> is each line's
    /// quantity on invoices not voided (see <see cref="RemainingToBill"/>). Food on a tax invoice is
    /// returned with a refund, not discarded off the tab.</para>
    ///
    /// <para><b>Phase 82 -- once the order's Amount shares are frozen, the discarded food's share leaves with
    /// it</b> (the user's decision of 2026-10-11): the order's amount falls by what the quantity carries
    /// (<see cref="ShareCarriedBy"/>, the rule a transfer and a parcel follow), and every other dish keeps the
    /// share and the price it was billed at. That is exactly what billing the dish and refunding it leaves, without
    /// the two tax documents. Before the first bill the amount is a term of the tab, still spread over whatever is
    /// on it, so a discard leaves it whole. Phase 74 refused the discard instead.</para>
    /// </summary>
    public IReadOnlyList<KitchenTicket> Discard(
        IReadOnlyList<PosOrderLineQuantity> items, string reason, Guid userId,
        IReadOnlyDictionary<Guid, PosOrderLineBilled> billed)
    {
        EnsureOpen();
        EnsureNotEmpty(items, "Choose what to discard.");
        EnsureDistinct(items);

        var changes = new List<(PosOrderLine Line, decimal Quantity)>();
        var shares = new List<(PosOrderLine Line, decimal Share)>();

        foreach (var item in items)
        {
            var line = FindLine(item.LineId);
            var quantity = RequireQuantity(item.Quantity);
            var net = QuantitiesOf(line).Net;
            var lineBilled = billed.GetValueOrDefault(line.Id);
            var invoiced = lineBilled?.Quantity ?? 0m;

            if (quantity > net - invoiced)
            {
                throw new InvalidOperationException(invoiced > 0m
                    ? $"Line {line.LineNo} has {net:0.####} on the order and {invoiced:0.####} of it billed, so at most "
                      + $"{net - invoiced:0.####} can be discarded. What is billed is refunded, not discarded."
                    : $"Line {line.LineNo} has {net:0.####} on the order, so {quantity:0.####} cannot be discarded.");
            }

            changes.Add((line, -quantity));

            // Read before the tickets change the quantity the share is a part of.
            if (DiscountSharesFixed)
            {
                shares.Add((line, ShareCarriedBy(line, quantity, lineBilled)));
            }
        }

        var tickets = Ticket(changes, userId, RequireReason(reason), KitchenTicketKind.Cancellation);

        foreach (var (line, share) in shares.Where(x => x.Share != 0m))
        {
            GiveUpShare(line, share);
        }

        return tickets;
    }

    /// <summary>
    /// Discards the whole order with a reason: every line's remaining quantity is cancelled to the
    /// kitchen, and the table is free again. The order and its tickets stay, so the ERP's POS Orders
    /// list shows what was cooked and why it was not billed.
    ///
    /// <para>Phase 65 -- refused once any of it is billed: voiding the order would leave invoices for
    /// an order that says it was never served. Discard what is left instead; the order settles when
    /// nothing unbilled remains.</para>
    /// </summary>
    public IReadOnlyList<KitchenTicket> Void(
        string reason, Guid userId, DateTimeOffset now, IReadOnlyDictionary<Guid, decimal> invoiced)
    {
        EnsureOpen();

        if (invoiced.Values.Any(x => x > 0m))
        {
            throw new InvalidOperationException(
                $"Part of order {Code} is already billed, so the order cannot be voided. Discard what is left "
                + "instead, and the order settles once nothing unbilled remains.");
        }

        var why = RequireReason(reason);
        var changes = _lines
            .Select(line => (Line: line, Quantity: -QuantitiesOf(line).Net))
            .Where(x => x.Quantity != 0m)
            .ToList();

        var tickets = changes.Count == 0 ? [] : Ticket(changes, userId, why, KitchenTicketKind.Cancellation);

        Status = PosOrderStatus.Voided;
        VoidReason = why;
        VoidedAt = now;
        VoidedByUserId = userId;
        Touch();

        return tickets;
    }

    /// <summary>
    /// Phase 68 -- parcels part of a dine-in line: the quantity moves to the line's take-away sibling,
    /// told to the kitchen as one ticket carrying minus on the dine-in line and plus on the sibling, so the
    /// kitchen packs it rather than plates it. The line still stores no quantity (phase 64 Decision B); the
    /// vendor's <c>takeaway_quantity</c> counter is the second view this avoids.
    ///
    /// <para><b>The service charge is decided here and frozen on the sibling</b> (the user's answer of
    /// 2026-10-03): it keeps the dine-in line's rate when the location's <i>Service charge on take-away</i>
    /// is on, and none when it is off. A product exempt from service charge was already at zero on the
    /// dine-in line, so it stays exempt without the catalogue being read again. A later change of the
    /// setting reprices nothing: a second mark at a different rate makes a second sibling.</para>
    ///
    /// <para><b>Only food not yet served and not yet billed.</b> Packing a plate already eaten from would
    /// take the service charge off food the table was served; and a billed quantity is on a tax invoice at
    /// its rate.</para>
    ///
    /// <para><b>Phase 70 -- the packaging fee is decided here too</b>, by the caller from the location's
    /// setting and the product's flag (<see cref="PosPackagingFee.ForParcel"/>), and frozen on the sibling
    /// beside its service charge: plated food never carried one, so there is nothing to copy.</para>
    ///
    /// <para><b>Phase 77 -- a parcel takes its share of the order's Amount discount with it</b>, as a transfer
    /// does: once the order's shares are frozen, the parcelled quantity's part of the dish's share moves to the
    /// sibling, and the order's amount is unchanged. Phase 74 refused the mark instead.</para>
    /// </summary>
    public PosOrderMoveResult MarkTakeAway(
        Guid lineId, decimal quantity, bool serviceChargeOnTakeAway, PackagingFee packagingFee, Guid userId,
        IReadOnlyDictionary<Guid, PosOrderLineBilled> billed)
    {
        EnsureOpen();

        if (OrderType != PosTab.DineIn)
        {
            throw new InvalidOperationException(
                $"A {Describe(OrderType)} order is parcelled already; only a Dine In order has items to mark as take away.");
        }

        var line = FindLine(lineId);
        if (line.IsTakeAway)
        {
            throw new InvalidOperationException($"Line {line.LineNo} is already take away.");
        }

        var q = RequireQuantity(quantity);
        var quantities = QuantitiesOf(line);
        var lineBilled = billed.GetValueOrDefault(line.Id);
        var markable = Math.Min(quantities.Outstanding, quantities.Net - (lineBilled?.Quantity ?? 0m));

        if (q > markable)
        {
            throw new InvalidOperationException(
                $"Line {line.LineNo} has {Math.Max(markable, 0m):0.####} not yet served or billed, so {q:0.####} cannot be "
                + "marked as take away. Served or billed food stays as it was.");
        }

        var rate = serviceChargeOnTakeAway ? line.ServiceChargeRate : 0m;
        var created = new List<PosOrderLine>();

        // Phase 72 -- the parcel keeps the line's discount (CopyOf): the same dish at the same terms. A sibling
        // whose discount was changed since is not those terms, so a second mark makes another.
        var parcel = _lines.FirstOrDefault(x =>
            x.ParcelledFromLineId == line.Id && x.ServiceChargeRate == rate && x.PackagingFee == packagingFee
            && x.DiscountPct == line.DiscountPct && x.DiscountPerUnit == line.DiscountPerUnit);

        if (parcel is null)
        {
            EnsureRoomFor(1);
            parcel = PosOrderLine.CopyOf(
                Id, NextLineNo(), line, isTakeAway: true, rate, packagingFee, parcelledFromLineId: line.Id);
            _lines.Add(parcel);
            created.Add(parcel);
        }

        var share = ShareCarriedBy(line, q, lineBilled);
        var tickets = Ticket([(line, -q), (parcel, q)], userId, reason: null, KitchenTicketKind.TakeAway);

        if (share != 0m)
        {
            GiveUpShare(line, share);
            TakeShare(parcel, share);
        }

        return new PosOrderMoveResult(created, tickets);
    }

    /// <summary>
    /// Phase 68 -- moves quantities from one Dine In order to another at the same location: the vendor's
    /// <i>Transfer Items</i>. Each order records it as a transfer ticket naming the other, minus on the
    /// source and plus on the target, so both orders' quantities stay sums over their own tickets.
    ///
    /// <para><b>Rates travel with the quantity.</b> The target's line copies the source line's frozen
    /// rate, VAT, service charge, unit, station, note and (phase 72) discount (merging into a target line
    /// whose every term is the same), so a dish costs the same on either tab.</para>
    ///
    /// <para><b>Phase 72 -- so do the tab's own terms.</b> An order's discount and its waived service charge
    /// price every line on it, so the two orders must agree on them, or the food would change price on the
    /// way. A target with nothing on it and no terms of its own (a table just seated, or the order a transfer
    /// to a free table opens) takes the source's; any other must already have them (a 409 naming both).</para>
    ///
    /// <para><b>Phase 77 -- and an Amount discount moves with the food</b> (phase-77-status.md Decision A). The
    /// moved quantity takes its share of the source's amount (<see cref="ShareCarriedBy"/>), the source's
    /// amount falls by it and the target's rises by it, so the two discounts still add up to what was given.
    /// "The same terms" for an amount is the kind, not the figure: both orders take rupees off, or the target is
    /// empty. Where an order's shares are frozen (it has been billed), the share moves between frozen line shares.
    /// </para>
    ///
    /// <para><b>Never more than is unbilled</b>, so the source can never fall below what its own invoices
    /// name, and a void of any bill gives its quantity back to the line that bill names. Unserved food moves
    /// first; any served food that moves takes its served count with it.</para>
    ///
    /// <para>A transfer that leaves the source with nothing on it and nothing billed voids it ("All items
    /// transferred to ORD…"), which frees its table; one that leaves it fully billed is settled by the
    /// caller (<see cref="SettleIfFullyBilled"/>).</para>
    /// </summary>
    public static PosOrderTransferResult Transfer(
        PosOrder source, PosOrder target, IReadOnlyList<PosOrderLineQuantity> items, Guid userId,
        IReadOnlyDictionary<Guid, PosOrderLineBilled> sourceBilled, DateTimeOffset now)
    {
        if (source.Id == target.Id)
        {
            throw new InvalidOperationException("Choose another table: items are transferred to a different order.");
        }

        source.EnsureOpen();
        target.EnsureOpen();

        if (source.OrderType != PosTab.DineIn || target.OrderType != PosTab.DineIn)
        {
            throw new InvalidOperationException("Items are transferred between Dine In orders only.");
        }

        if (source.OrganizationId != target.OrganizationId || source.BillingLocationId != target.BillingLocationId)
        {
            throw new InvalidOperationException("Items are transferred between tables at the same location.");
        }

        EnsureNotEmpty(items, "Choose what to transfer.");
        EnsureDistinct(items);

        target.TakeTermsFor(source);

        var outgoing = new List<(PosOrderLine Line, decimal Quantity)>();
        var incoming = new List<(PosOrderLine Line, decimal Quantity)>();
        var servedMoving = new List<(PosOrderLine Line, decimal Served)>();
        var sharesMoving = new List<(PosOrderLine From, PosOrderLine To, decimal Share)>();
        var created = new List<PosOrderLine>();

        foreach (var item in items)
        {
            var line = source.FindLine(item.LineId);
            var q = RequireQuantity(item.Quantity);
            var quantities = source.QuantitiesOf(line);
            var billed = sourceBilled.GetValueOrDefault(line.Id);
            var transferable = quantities.Net - (billed?.Quantity ?? 0m);

            if (q > transferable)
            {
                throw new InvalidOperationException(
                    $"Line {line.LineNo} has {Math.Max(transferable, 0m):0.####} not yet billed, so {q:0.####} cannot be transferred.");
            }

            var destination = target._lines.FirstOrDefault(x => x.HasSameTermsAs(line));
            if (destination is null)
            {
                target.EnsureRoomFor(1);
                destination = PosOrderLine.CopyOf(
                    target.Id, target.NextLineNo(), line, line.IsTakeAway, line.ServiceChargeRate, line.PackagingFee,
                    parcelledFromLineId: null);
                target._lines.Add(destination);
                created.Add(destination);
            }

            outgoing.Add((line, -q));
            incoming.Add((destination, q));
            servedMoving.Add((destination, Math.Max(0m, q - quantities.Outstanding)));

            // Read before the tickets move anything: a share is of the line as it stands.
            sharesMoving.Add((line, destination, source.ShareCarriedBy(line, q, billed)));
        }

        var sourceTickets = source.Ticket(outgoing, userId, reason: null, KitchenTicketKind.Transfer, target.Id);
        var targetTickets = target.Ticket(incoming, userId, reason: null, KitchenTicketKind.Transfer, source.Id);

        foreach (var (line, served) in servedMoving.Where(x => x.Served > 0m))
        {
            line.AddServed(served);
        }

        foreach (var (from, to, share) in sharesMoving.Where(x => x.Share != 0m))
        {
            source.GiveUpShare(from, share);
            target.TakeShare(to, share);
        }

        var emptied = source._lines.All(x => source.QuantitiesOf(x).Net == 0m)
            && sourceBilled.Values.All(x => x.Quantity == 0m);

        if (emptied)
        {
            source.Status = PosOrderStatus.Voided;
            source.VoidReason = $"All items transferred to {target.Code}";
            source.VoidedAt = now;
            source.VoidedByUserId = userId;
        }

        return new PosOrderTransferResult(
            new PosOrderMoveResult([], sourceTickets), new PosOrderMoveResult(created, targetTickets), emptied);
    }

    /// <summary>Moves a Dine In order to another table, as the vendor's table transfer does.</summary>
    public void MoveToTable(Guid tableId)
    {
        EnsureOpen();

        if (OrderType != PosTab.DineIn)
        {
            throw new InvalidOperationException($"A {Describe(OrderType)} order has no table to move.");
        }

        PosTableId = tableId;
        Touch();
    }

    public void UpdateDetails(int covers, Guid? contactId)
    {
        EnsureOpen();
        EnsureCovers(OrderType, covers);

        if (OrderType == PosTab.Delivery && contactId is null)
        {
            throw new InvalidOperationException("A Delivery order names the customer it goes to.");
        }

        Covers = covers;
        ContactId = contactId;
        Touch();
    }

    /// <summary>
    /// Phase 72 -- the order's discount (the vendor's <i>Options &gt; Discount</i>, read live on 2026-10-09):
    /// a percentage off every line after its own. <b>Only while nothing on the order is billed.</b> A part
    /// already paid was priced at the old terms, and the planner gives the last of each line exactly what is
    /// left of it, so a change now would land on the last part alone. Who may give one is the caller's check
    /// (<c>Sales.Invoice.TillDiscount</c> at the order's location). Setting the value it already has changes nothing.
    /// </summary>
    public void SetDiscount(decimal discountPct, IReadOnlyDictionary<Guid, decimal> invoiced) =>
        SetDiscount(PosDiscountKind.Percent, discountPct, invoiced);

    /// <summary>Phase 74 -- the order's discount as a percentage or as an amount (<see cref="DiscountAmount"/>),
    /// either one replacing the other. An amount is at most what the order comes to now.</summary>
    public void SetDiscount(PosDiscountKind kind, decimal value, IReadOnlyDictionary<Guid, decimal> invoiced)
    {
        EnsureOpen();
        var (pct, amount) = kind switch
        {
            PosDiscountKind.Percent => (RequireDiscountPct(value), 0m),
            PosDiscountKind.Amount => (0m, RequireDiscountAmount(value)),
            _ => throw new InvalidOperationException($"'{kind}' is not a kind of discount."),
        };

        if (pct == DiscountPct && amount == DiscountAmount)
        {
            return;
        }

        if (_lines.Any(x => invoiced.GetValueOrDefault(x.Id) > 0m))
        {
            throw new InvalidOperationException(
                $"Part of order {Code} is already billed, so its discount is fixed: every part is priced at the same "
                + "terms, or the bills would not add up to the order.");
        }

        var comesTo = _lines.Sum(x => PosLineArithmetic.NetOf(
            QuantitiesOf(x).Net, x.Rate, x.DiscountPerUnit, x.DiscountPct, 0m));
        if (amount > comesTo)
        {
            throw new InvalidOperationException(
                $"Order {Code} comes to {comesTo:0.00} before VAT, so a discount of {amount:0.00} is more than the order.");
        }

        DiscountPct = pct;
        DiscountAmount = amount;
        Touch();
    }

    /// <summary>
    /// Phase 72 -- one line's discount (the vendor's <i>Edit Item &gt; Discount (Item Line)</i>): a percentage
    /// off that dish, before the order's. Only while nothing of that line is billed, for the reason
    /// <see cref="SetDiscount"/> gives; the rest of the order is free to be billed already.
    /// </summary>
    public void SetLineDiscount(Guid lineId, decimal discountPct, IReadOnlyDictionary<Guid, decimal> invoiced) =>
        SetLineDiscount(lineId, PosDiscountKind.Percent, discountPct, invoiced);

    /// <summary>Phase 74 -- a dish's discount as a percentage or as an amount off each unit
    /// (<see cref="PosOrderLine.DiscountPerUnit"/>, as the vendor's <i>Edit Item</i> takes it), never more than
    /// the dish's rate.</summary>
    public void SetLineDiscount(
        Guid lineId, PosDiscountKind kind, decimal value, IReadOnlyDictionary<Guid, decimal> invoiced)
    {
        EnsureOpen();
        var line = FindLine(lineId);
        var (pct, perUnit) = kind switch
        {
            PosDiscountKind.Percent => (RequireDiscountPct(value), 0m),
            PosDiscountKind.Amount => (0m, RequireDiscountAmount(value)),
            _ => throw new InvalidOperationException($"'{kind}' is not a kind of discount."),
        };

        if (pct == line.DiscountPct && perUnit == line.DiscountPerUnit)
        {
            return;
        }

        if (invoiced.GetValueOrDefault(line.Id) > 0m)
        {
            throw new InvalidOperationException(
                $"Part of line {line.LineNo} is already billed, so its discount is fixed: every part of a line is priced "
                + "at the same terms.");
        }

        if (perUnit > line.Rate)
        {
            throw new InvalidOperationException(
                $"Line {line.LineNo} is {line.Rate:0.00} a unit, so {perUnit:0.00} off each is more than the dish.");
        }

        // A dish not yet billed may still be discounted after its neighbours are paid (phase 72), but never below
        // the share of the order's amount discount it took when the order was first billed.
        if (line.OrderDiscountShare is { } share
            && PosLineArithmetic.NetOf(QuantitiesOf(line).Net, line.Rate, perUnit, pct, 0m) < share)
        {
            throw new InvalidOperationException(
                $"Line {line.LineNo} carries {share:0.00} of the order's discount, fixed at its first bill, so it cannot "
                + "be discounted below that.");
        }

        line.SetDiscount(pct, perUnit);
        Touch();
    }

    /// <summary>
    /// Phase 74 -- whether the order's Amount discount is spread for good: from its first bill, each line keeps the
    /// share it had then (<see cref="FixDiscountShares"/>), and a dish sent later carries none.
    /// </summary>
    public bool DiscountSharesFixed => _lines.Any(x => x.OrderDiscountShare is not null);

    /// <summary>
    /// Phase 74 -- this line's share of <see cref="DiscountAmount"/>: the frozen one once the order is billed, and
    /// until then the order's amount spread over its dishes as they stand (<see cref="PosLineArithmetic.Spread"/>,
    /// by what each comes to after its own discount). Dishes discarded before the first bill can leave the order
    /// coming to less than its amount; the spread then gives each line all of itself, and no more.
    /// </summary>
    public decimal DiscountShareOf(PosOrderLine line)
    {
        if (DiscountSharesFixed)
        {
            return line.OrderDiscountShare ?? 0m;
        }

        return DiscountAmount == 0m ? 0m : LiveDiscountShares().GetValueOrDefault(line.Id);
    }

    /// <summary>Phase 74 -- the part of a line's share that <paramref name="quantity"/> of it carries.</summary>
    public decimal DiscountShareOf(PosOrderLine line, decimal quantity) =>
        PosLineArithmetic.Prorate(DiscountShareOf(line), quantity, QuantitiesOf(line).Net);

    /// <summary>
    /// Phase 74 -- freezes each line's share of the order's Amount discount, called with the order's first bill.
    /// Before it, the spread follows the dishes; after it, a bill already paid was priced with these shares, so
    /// moving them would land the difference on whichever part came last. Does nothing without an amount, or a
    /// second time.
    /// </summary>
    public void FixDiscountShares()
    {
        if (DiscountAmount == 0m || DiscountSharesFixed)
        {
            return;
        }

        var shares = LiveDiscountShares();
        foreach (var line in _lines)
        {
            line.FixDiscountShare(shares.GetValueOrDefault(line.Id));
        }

        Touch();
    }

    /// <summary>Phase 74 -- a void of every bill unfixes the order's terms (phase 72), its discount shares
    /// included: the spread follows the dishes again until the next first bill.</summary>
    public void ReleaseDiscountShares(IReadOnlyDictionary<Guid, decimal> invoiced)
    {
        if (!DiscountSharesFixed || _lines.Any(x => invoiced.GetValueOrDefault(x.Id) > 0m))
        {
            return;
        }

        foreach (var line in _lines)
        {
            line.FixDiscountShare(null);
        }

        Touch();
    }

    private Dictionary<Guid, decimal> LiveDiscountShares()
    {
        var bases = _lines
            .Select(x => PosLineArithmetic.NetOf(QuantitiesOf(x).Net, x.Rate, x.DiscountPerUnit, x.DiscountPct, 0m))
            .ToList();
        var shares = PosLineArithmetic.Spread(Math.Min(DiscountAmount, bases.Sum()), bases);
        return _lines.Select((x, i) => (x.Id, shares[i])).ToDictionary(x => x.Id, x => x.Item2);
    }

    /// <summary>
    /// Phase 77 -- what <paramref name="quantity"/> of a line's unbilled food carries of the order's Amount discount
    /// when it moves (a transfer, or a parcel). Before the first bill it is the quantity's part of the line's live
    /// share. Once the shares are frozen it is the quantity's part of what the line's bills have not yet taken, and
    /// moving all that is left takes all of it: the rule a bill's parts follow (<see cref="PosOrderBill"/>), so the
    /// food that stays keeps the same share a unit and its last part still takes exactly what is left.
    /// </summary>
    private decimal ShareCarriedBy(PosOrderLine line, decimal quantity, PosOrderLineBilled? billed)
    {
        if (DiscountAmount == 0m)
        {
            return 0m;
        }

        if (!DiscountSharesFixed)
        {
            return DiscountShareOf(line, quantity);
        }

        var unbilledShare = (line.OrderDiscountShare ?? 0m) - (billed?.BillDiscountAmount ?? 0m);
        var unbilled = QuantitiesOf(line).Net - (billed?.Quantity ?? 0m);

        return quantity == unbilled
            ? unbilledShare
            : PosLineArithmetic.Prorate(unbilledShare, quantity, unbilled);
    }

    /// <summary>Phase 77 -- a share leaving this order with its food: off the line's frozen share when the shares
    /// are frozen, and off the order's amount either way. Phase 82 -- a discard's food leaves for nowhere, and so
    /// does its share.</summary>
    private void GiveUpShare(PosOrderLine line, decimal share)
    {
        if (DiscountSharesFixed)
        {
            line.FixDiscountShare((line.OrderDiscountShare ?? 0m) - share);
        }

        DiscountAmount -= share;
        Touch();
    }

    /// <summary>Phase 77 -- a share arriving with its food: frozen on the line it joins when this order's shares are
    /// frozen (a dish sent after the first bill would carry none, but this one brings its own), and onto the order's
    /// amount either way. Before the first bill the amount is spread over the dishes as they stand, as always.</summary>
    private void TakeShare(PosOrderLine line, decimal share)
    {
        if (DiscountSharesFixed)
        {
            line.FixDiscountShare((line.OrderDiscountShare ?? 0m) + share);
        }

        DiscountAmount += share;
        Touch();
    }

    /// <summary>
    /// Phase 72 -- the guest declined the service charge (<paramref name="waived"/>), or changed their mind.
    /// Fixed from the first bill, like <see cref="SetDiscount"/>: half an order's parts with it and half without
    /// is two tabs' terms on one.
    /// </summary>
    public void WaiveServiceCharge(bool waived, IReadOnlyDictionary<Guid, decimal> invoiced)
    {
        EnsureOpen();

        if (waived == ServiceChargeWaived)
        {
            return;
        }

        if (_lines.Any(x => invoiced.GetValueOrDefault(x.Id) > 0m))
        {
            throw new InvalidOperationException(
                $"Part of order {Code} is already billed with its service charge {(ServiceChargeWaived ? "waived" : "charged")}, "
                + "so that is fixed: every part is priced at the same terms.");
        }

        ServiceChargeWaived = waived;
        Touch();
    }

    /// <summary>Phase 72 -- the service charge rate a line is billed at: its own frozen rate, or none when the
    /// guest declined it.</summary>
    public decimal ServiceChargeRateOf(PosOrderLine line) => ServiceChargeWaived ? 0m : line.ServiceChargeRate;

    /// <summary>
    /// A line's money at <paramref name="quantity"/>, by the till's one arithmetic: its frozen rate, VAT, service
    /// charge and packaging fee, and (phase 72) its own discount, the order's, and the waiver. The one place an
    /// order line is priced -- the estimate, the screens and the bill planner all come here -- so no reader can
    /// price a line without the order's terms.
    /// </summary>
    public PosLineArithmetic.Figures FiguresOf(PosOrderLine line, decimal quantity) =>
        PosLineArithmetic.Compute(
            quantity, line.Rate, line.VatRate.ToPercent(), line.DiscountPerUnit, line.DiscountPct, DiscountPct,
            DiscountShareOf(line, quantity), ServiceChargeRateOf(line), line.PackagingFee);

    /// <summary>Counts one more print of a ticket and returns its number: 1 is the original, and the
    /// paper marks anything after it a reprint, so a kitchen does not cook one ticket twice.</summary>
    public int RecordTicketPrint(Guid ticketId)
    {
        var ticket = _tickets.SingleOrDefault(x => x.Id == ticketId)
            ?? throw new InvalidOperationException("That kitchen ticket is not on this order.");

        Touch();
        return ticket.RecordPrint();
    }

    public PosOrderLineQuantities QuantitiesOf(PosOrderLine line)
    {
        decimal ordered = 0m, discarded = 0m, movedIn = 0m, movedOut = 0m;

        foreach (var ticket in _tickets)
        {
            foreach (var row in ticket.Lines)
            {
                if (row.PosOrderLineId != line.Id)
                {
                    continue;
                }

                // Phase 68 -- a move is neither ordered nor discarded: it is the same food on another line.
                var isMove = ticket.Kind is KitchenTicketKind.TakeAway or KitchenTicketKind.Transfer;

                if (row.Quantity > 0m)
                {
                    if (isMove)
                    {
                        movedIn += row.Quantity;
                    }
                    else
                    {
                        ordered += row.Quantity;
                    }
                }
                else if (isMove)
                {
                    movedOut -= row.Quantity;
                }
                else
                {
                    discarded -= row.Quantity;
                }
            }
        }

        return new PosOrderLineQuantities(
            ordered, discarded, ordered + movedIn - discarded - movedOut, line.ServedQuantity, movedIn, movedOut);
    }

    /// <summary>
    /// Phase 65 -- what of this line is still to be billed: its net quantity less what is on invoices not
    /// voided. <paramref name="invoiced"/> is a sum over invoice lines naming the order line, read by the
    /// caller (phase 64 Decision B: invoiced is not a column here, so it cannot drift from the invoices,
    /// and a voided invoice gives its quantity back without anyone writing a counter).
    /// </summary>
    public decimal RemainingToBill(PosOrderLine line, IReadOnlyDictionary<Guid, decimal> invoiced) =>
        QuantitiesOf(line).Net - invoiced.GetValueOrDefault(line.Id);

    /// <summary>
    /// Phase 65 -- settles the order when nothing unbilled remains and something was billed, which frees
    /// its table (the one-open-order index is on <see cref="PosOrderStatus.Open"/>). Called after every
    /// bill and every discard. An order with nothing on it and no bill stays open: the waiter may still
    /// add to it, or void it.
    /// </summary>
    /// <returns>Whether this call settled it.</returns>
    public bool SettleIfFullyBilled(IReadOnlyDictionary<Guid, decimal> invoiced, DateTimeOffset now)
    {
        if (Status != PosOrderStatus.Open)
        {
            return false;
        }

        var anyBilled = _lines.Any(x => invoiced.GetValueOrDefault(x.Id) > 0m);
        var anyLeft = _lines.Any(x => RemainingToBill(x, invoiced) != 0m);

        if (!anyBilled || anyLeft)
        {
            return false;
        }

        Status = PosOrderStatus.Settled;
        SettledAt = now;
        Touch();
        return true;
    }

    /// <summary>
    /// Phase 65 -- a void of one of a settled order's invoices gives its quantities back, so the order is
    /// open again, to be billed anew (or discarded). Whether its table is still free is the caller's
    /// question, because only the database can answer it.
    /// </summary>
    public void Reopen()
    {
        if (Status != PosOrderStatus.Settled)
        {
            return;
        }

        Status = PosOrderStatus.Open;
        SettledAt = null;
        Touch();
    }

    /// <summary>
    /// Phase 65 -- where each send ticket stands on the kitchen board, derived from the one quantity and
    /// the one counter the order already stores (phase 64 Decision B): a line's served count is given to
    /// its <b>earliest</b> sends, because the kitchen cooks in the order it was told; and what was
    /// discarded is taken from its <b>latest</b> unserved sends, because a discard cancels unserved food
    /// first and the newest is the least likely to be on the stove. Nothing here is stored, so the board
    /// can never disagree with the order. The vendor instead hides a ticket unserved for five hours
    /// ("Archived", a client-side age test), which is a dish silently forgotten.
    /// </summary>
    public IReadOnlyList<KitchenTicketProgress> TicketProgress()
    {
        // Phase 68 -- what brought food onto a line: a send, or the incoming half of a take-away mark or a
        // transfer. What took it off unserved: a discard (cancelled), or the outgoing half of a move (moved).
        var inOrder = _tickets
            .OrderBy(x => x.SendNumber)
            .ThenBy(x => x.CreatedAt)
            .ToList();

        var allocated = new Dictionary<(Guid TicketId, Guid LineId), (decimal Served, decimal Cancelled, decimal Moved)>();

        foreach (var line in _lines)
        {
            var rows = inOrder
                .SelectMany(t => t.Lines.Where(r => r.PosOrderLineId == line.Id && r.Quantity > 0m).Select(r => (Ticket: t, Row: r)))
                .ToList();

            var q = QuantitiesOf(line);
            var served = q.Served;
            var cancelled = q.Discarded;
            var moved = q.MovedOut;
            var cut = new Dictionary<Guid, (decimal Served, decimal Cancelled, decimal Moved)>();

            foreach (var (ticket, row) in rows)
            {
                var take = Math.Min(row.Quantity, served);
                served -= take;
                cut[ticket.Id] = (take, 0m, 0m);
            }

            for (var i = rows.Count - 1; i >= 0; i--)
            {
                var (ticket, row) = rows[i];
                var (s, _, _) = cut[ticket.Id];
                var c = Math.Min(row.Quantity - s, cancelled);
                cancelled -= c;
                var m = Math.Min(row.Quantity - s - c, moved);
                moved -= m;
                cut[ticket.Id] = (s, c, m);
            }

            foreach (var (ticketId, value) in cut)
            {
                allocated[(ticketId, line.Id)] = value;
            }
        }

        return _tickets
            .OrderBy(x => x.SendNumber)
            .ThenBy(x => x.CreatedAt)
            .Select(ticket =>
            {
                if (ticket.Lines.All(r => r.Quantity < 0m))
                {
                    return new KitchenTicketProgress(
                        ticket.Id,
                        ticket.Kind == KitchenTicketKind.Cancellation ? KitchenTicketState.Cancellation : KitchenTicketState.Moved,
                        [.. ticket.Lines.Select(r => new KitchenTicketLineProgress(r.PosOrderLineId, r.Quantity, 0m, 0m))]);
                }

                // A take-away ticket's minus line (the dine-in half) is shown for what it is and owes nothing.
                var lines = ticket.Lines
                    .Select(r =>
                    {
                        if (r.Quantity < 0m)
                        {
                            return new KitchenTicketLineProgress(r.PosOrderLineId, r.Quantity, 0m, 0m);
                        }

                        var (s, c, m) = allocated.GetValueOrDefault((ticket.Id, r.PosOrderLineId));
                        return new KitchenTicketLineProgress(r.PosOrderLineId, r.Quantity, s, c, m);
                    })
                    .ToList();

                var owed = lines.Where(x => x.Sent > 0m).ToList();
                var state = owed.Any(x => x.Pending > 0m) ? KitchenTicketState.Pending
                    : owed.All(x => x.Served == 0m)
                        ? (owed.All(x => x.Cancelled == 0m) && owed.Any(x => x.Moved > 0m) ? KitchenTicketState.Moved : KitchenTicketState.Cancelled)
                        : KitchenTicketState.Served;

                return new KitchenTicketProgress(ticket.Id, state, lines);
            })
            .ToList();
    }

    /// <summary>The order's running figures before any bill: each line's net quantity priced by phase
    /// 63's <see cref="PosLineArithmetic"/> (<see cref="FiguresOf"/>). Unrounded to the rupee -- the bill
    /// (phase 65) rounds.</summary>
    public PosLineArithmetic.Figures Estimate() =>
        _lines.Aggregate(PosLineArithmetic.Figures.Zero, (sum, line) => sum + FiguresOf(line, QuantitiesOf(line).Net));

    public static string Describe(PosTab orderType) => orderType switch
    {
        PosTab.DineIn => "Dine In",
        PosTab.TakeAway => "Take Away",
        PosTab.Delivery => "Delivery",
        _ => orderType.ToString(),
    };

    private List<KitchenTicket> Ticket(
        List<(PosOrderLine Line, decimal Quantity)> changes, Guid userId, string? reason, KitchenTicketKind kind,
        Guid? counterpartOrderId = null)
    {
        var sendNumber = _tickets.Count == 0 ? 1 : _tickets.Max(x => x.SendNumber) + 1;
        var now = DateTimeOffset.UtcNow;

        var tickets = changes
            .GroupBy(x => x.Line.KitchenStationId)
            .OrderBy(g => g.Key is null ? 1 : 0)
            .Select(g => KitchenTicket.Create(
                Id, sendNumber, g.Key, [.. g.Select(x => (x.Line.Id, x.Quantity))], userId, reason, now, kind, counterpartOrderId))
            .ToList();

        _tickets.AddRange(tickets);

        // A cancellation below what was served takes the served count down with it (see Discard).
        foreach (var (line, _) in changes)
        {
            var net = QuantitiesOf(line).Net;
            if (line.ServedQuantity > net)
            {
                line.ReduceServedTo(net);
            }
        }

        Touch();
        return tickets;
    }

    private int NextLineNo() => _lines.Count == 0 ? 1 : _lines.Max(x => x.LineNo) + 1;

    private void EnsureRoomFor(int newLines)
    {
        if (_lines.Count + newLines > MaxLines)
        {
            throw new InvalidOperationException($"An order holds at most {MaxLines} lines.");
        }
    }

    private PosOrderLine FindLine(Guid lineId) =>
        _lines.SingleOrDefault(x => x.Id == lineId)
        ?? throw new InvalidOperationException("That line is not on this order.");

    private void EnsureOpen()
    {
        if (Status != PosOrderStatus.Open)
        {
            throw new InvalidOperationException($"Order {Code} is {Status.ToString().ToLowerInvariant()}, so it cannot change.");
        }
    }

    private void EnsureNotVoided()
    {
        if (Status == PosOrderStatus.Voided)
        {
            throw new InvalidOperationException($"Order {Code} is voided, so it cannot change.");
        }
    }

    /// <summary>Phase 65 -- marks the order as changed, so its rowversion moves: a bill touches it before
    /// saving, and two cashiers billing one order at once cannot both be counted against the same
    /// remainder (the second save is a concurrency 409).</summary>
    public void Touch() => LastActivityAt = DateTimeOffset.UtcNow;

    private static void EnsureCovers(PosTab orderType, int covers)
    {
        var min = orderType == PosTab.DineIn ? 1 : 0;

        if (covers < min || covers > MaxCovers)
        {
            throw new InvalidOperationException(orderType == PosTab.DineIn
                ? $"A Dine In order seats between 1 and {MaxCovers} guests."
                : $"Guests on an order are between 0 and {MaxCovers}.");
        }
    }

    private static decimal RequireQuantity(decimal quantity)
    {
        if (quantity <= 0m)
        {
            throw new InvalidOperationException("A quantity must be more than zero.");
        }

        if (decimal.Round(quantity, UnitConversion.QuantityScale) != quantity)
        {
            throw new InvalidOperationException($"A quantity has at most {UnitConversion.QuantityScale} decimal places.");
        }

        return quantity;
    }

    /// <summary>Phase 74 -- an amount discount is whole paisa and never negative; what it may not exceed is the
    /// caller's to say.</summary>
    private static decimal RequireDiscountAmount(decimal amount)
    {
        if (amount < 0m || decimal.Round(amount, Invoice.PosMoneyScale) != amount)
        {
            throw new InvalidOperationException("A discount amount is whole paisa and never negative.");
        }

        return amount;
    }

    /// <summary>Phase 72 -- a discount is a percentage from 0 to 100 with at most two decimal places, as a
    /// cashier types one.</summary>
    private static decimal RequireDiscountPct(decimal discountPct)
    {
        if (discountPct < 0m || discountPct > 100m)
        {
            throw new InvalidOperationException("A discount is between 0% and 100%.");
        }

        if (decimal.Round(discountPct, MaxDiscountScale) != discountPct)
        {
            throw new InvalidOperationException($"A discount has at most {MaxDiscountScale} decimal places.");
        }

        return discountPct;
    }

    /// <summary>Phase 72 -- see <see cref="Transfer"/>: a target with nothing on it and no terms of its own takes
    /// the source's; any other must already have them.</summary>
    private void TakeTermsFor(PosOrder source)
    {
        // Phase 77 -- an Amount is the same terms as another Amount: the food brings its own share (Transfer), so the
        // figures need not agree, only that both orders take rupees off.
        if (DiscountPct == source.DiscountPct
            && (DiscountAmount != 0m) == (source.DiscountAmount != 0m)
            && ServiceChargeWaived == source.ServiceChargeWaived)
        {
            return;
        }

        if (_lines.Count == 0 && DiscountPct == 0m && DiscountAmount == 0m && !ServiceChargeWaived)
        {
            DiscountPct = source.DiscountPct;
            ServiceChargeWaived = source.ServiceChargeWaived;
            return;
        }

        throw new InvalidOperationException(
            $"Order {Code} is priced with {DescribeTerms()}, and order {source.Code} with {source.DescribeTerms()}. "
            + "Moving food between them would change its price, so give both orders the same discount and service "
            + "charge first.");
    }

    private string DescribeTerms() =>
        (DiscountPct > 0m ? $"a {DiscountPct:0.##}% discount"
            : DiscountAmount > 0m ? $"a discount of {DiscountAmount:0.00}"
            : "no discount")
        + (ServiceChargeWaived ? " and the service charge waived" : " and service charge");

    private static string RequireReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("A discard needs a reason.");
        }

        if (trimmed.Length > MaxReasonLength)
        {
            throw new InvalidOperationException($"A reason is at most {MaxReasonLength} characters.");
        }

        return trimmed;
    }

    private static void EnsureNotEmpty(IReadOnlyList<PosOrderLineQuantity> items, string message)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void EnsureDistinct(IReadOnlyList<PosOrderLineQuantity> items)
    {
        if (items.Select(x => x.LineId).Distinct().Count() != items.Count)
        {
            throw new InvalidOperationException("A line is named twice.");
        }
    }
}

/// <summary>
/// Phase 64 -- one thing ordered: a product, its unit, the price it was ordered at and the kitchen it
/// goes to, all frozen when it was first sent (phase 52's rule: a line stores what applied when it was
/// written). It stores no quantity: see <see cref="PosOrder"/>.
///
/// <para><b>The kitchen note is the line's</b> ("less spicy"), which fixes the vendor's defect 9: it
/// posts whole product objects as order lines, so its note lands in the product's
/// <c>description</c>.</para>
///
/// <para><b>The rate is the catalogue's, never the waiter's.</b> The command carries no rate; the
/// handler prices the line from the product, so a tab cannot be cheapened at the table. Phase 72 -- a
/// discount is a term of its own (<see cref="DiscountPct"/>), given by someone who may approve invoices at
/// the location, and priced with the order's own terms by <see cref="PosOrder.FiguresOf"/>.</para>
/// </summary>
public sealed class PosOrderLine
{
    public Guid Id { get; private set; }
    public Guid PosOrderId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? UnitId { get; private set; }
    public decimal ConversionFactor { get; private set; }

    /// <summary>VAT-exclusive, per <see cref="UnitId"/>, after the tenant's price basis.</summary>
    public decimal Rate { get; private set; }

    public VatRate VatRate { get; private set; }

    /// <summary>Frozen from <see cref="PosServiceCharge.RateFor"/> when the line was first sent, so a bill
    /// or a split (phase 65) moves quantities and never re-decides the rate (the vendor's defect 1).</summary>
    public decimal ServiceChargeRate { get; private set; }

    /// <summary>Phase 70 -- frozen from <see cref="PosPackagingFee"/> when the line was first sent (a Take
    /// Away or Delivery order) or marked (a parcel), for the reason <see cref="ServiceChargeRate"/> is.</summary>
    public PackagingFeeBasis PackagingFeeBasis { get; private set; }

    /// <inheritdoc cref="PackagingFeeBasis"/>
    public decimal PackagingFeeValue { get; private set; }

    public PackagingFee PackagingFee => PackagingFee.Create(PackagingFeeBasis, PackagingFeeValue);

    /// <summary>Phase 72 -- this dish's own discount, a percentage off its amount before the order's
    /// (<see cref="PosOrder.SetLineDiscount"/>). Zero when sent; fixed once any of the line is billed.</summary>
    public decimal DiscountPct { get; private set; }

    /// <summary>Phase 74 -- this dish's Amount discount, rupees off each unit, set like <see cref="DiscountPct"/>
    /// and never beside it.</summary>
    public decimal DiscountPerUnit { get; private set; }

    /// <summary>Phase 74 -- this dish's share of its order's <see cref="PosOrder.DiscountAmount"/>, frozen at the
    /// order's first bill (<see cref="PosOrder.FixDiscountShares"/>); null while the order is unbilled, and on a dish
    /// sent after that, which carries none.</summary>
    public decimal? OrderDiscountShare { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Where this line's tickets go: the product's station when first sent; null is Default.</summary>
    public Guid? KitchenStationId { get; private set; }

    /// <summary>How much has left the kitchen for the table. The one quantity stored on a line.</summary>
    public decimal ServedQuantity { get; private set; }

    /// <summary>Phase 68 -- parcelled, not plated: a dine-in order's take-away line (see
    /// <see cref="PosOrder.MarkTakeAway"/>). Its <see cref="ServiceChargeRate"/> was decided when it was
    /// marked.</summary>
    public bool IsTakeAway { get; private set; }

    /// <summary>Phase 68 -- the dine-in line a take-away line was parcelled from, on the same order; null for
    /// every other line, including a take-away line that arrived by transfer.</summary>
    public Guid? ParcelledFromLineId { get; private set; }

    private PosOrderLine()
    {
    }

    internal static PosOrderLine Create(Guid orderId, int lineNo, PosOrderNewLine row)
    {
        if (row.Rate < 0m)
        {
            throw new InvalidOperationException("A rate cannot be negative.");
        }

        if (row.ServiceChargeRate < 0m || row.ServiceChargeRate > 100m)
        {
            throw new InvalidOperationException("A service charge rate must be between 0% and 100%.");
        }

        var note = string.IsNullOrWhiteSpace(row.Note) ? null : row.Note.Trim();
        if (note is { Length: > PosOrder.MaxNoteLength })
        {
            throw new InvalidOperationException($"A kitchen note is at most {PosOrder.MaxNoteLength} characters.");
        }

        return new PosOrderLine
        {
            Id = Guid.NewGuid(),
            PosOrderId = orderId,
            LineNo = lineNo,
            ProductId = row.ProductId,
            UnitId = row.UnitId,
            ConversionFactor = UnitConversion.Validate(row.ConversionFactor),
            Rate = row.Rate,
            VatRate = row.VatRate,
            ServiceChargeRate = row.ServiceChargeRate,
            PackagingFeeBasis = row.PackagingFee.Basis,
            PackagingFeeValue = row.PackagingFee.Value,
            Note = note,
            KitchenStationId = row.KitchenStationId,
        };
    }

    /// <summary>Phase 68 -- a new line carrying another's frozen terms: a take-away sibling (its own service
    /// charge rate and, phase 70, its own packaging fee), or a transfer's destination on another order (the
    /// same terms). Phase 72 -- either way it keeps the source's discount: the same dish at the same terms.</summary>
    internal static PosOrderLine CopyOf(
        Guid orderId, int lineNo, PosOrderLine source, bool isTakeAway, decimal serviceChargeRate,
        PackagingFee packagingFee, Guid? parcelledFromLineId) =>
        new()
        {
            Id = Guid.NewGuid(),
            PosOrderId = orderId,
            LineNo = lineNo,
            ProductId = source.ProductId,
            UnitId = source.UnitId,
            ConversionFactor = source.ConversionFactor,
            Rate = source.Rate,
            VatRate = source.VatRate,
            ServiceChargeRate = serviceChargeRate,
            PackagingFeeBasis = packagingFee.Basis,
            PackagingFeeValue = packagingFee.Value,
            DiscountPct = source.DiscountPct,
            DiscountPerUnit = source.DiscountPerUnit,
            Note = source.Note,
            KitchenStationId = source.KitchenStationId,
            IsTakeAway = isTakeAway,
            ParcelledFromLineId = parcelledFromLineId,
        };

    /// <summary>Phase 68 -- whether a transferred quantity can join this line: every frozen term the same.</summary>
    internal bool HasSameTermsAs(PosOrderLine other) =>
        ProductId == other.ProductId
        && UnitId == other.UnitId
        && ConversionFactor == other.ConversionFactor
        && Rate == other.Rate
        && VatRate == other.VatRate
        && ServiceChargeRate == other.ServiceChargeRate
        && PackagingFee == other.PackagingFee
        && DiscountPct == other.DiscountPct
        && DiscountPerUnit == other.DiscountPerUnit
        && Note == other.Note
        && KitchenStationId == other.KitchenStationId
        && IsTakeAway == other.IsTakeAway;

    // Phase 72 -- a line's money is priced by its order (PosOrder.FiguresOf), which alone knows the order's
    // discount and whether the guest declined the service charge.

    internal void SetDiscount(decimal discountPct, decimal discountPerUnit)
    {
        DiscountPct = discountPct;
        DiscountPerUnit = discountPerUnit;
    }

    internal void FixDiscountShare(decimal? share) => OrderDiscountShare = share;

    internal void AddServed(decimal quantity) => ServedQuantity += quantity;

    internal void ReduceServedTo(decimal quantity) => ServedQuantity = quantity;
}

/// <summary>
/// Phase 64 -- what one send told one kitchen station: the vendor's KOT, one per save per station
/// (phase 59 Decision F). A send's tickets share a <see cref="SendNumber"/>, which is what the paper
/// prints after the order's code (<c>ORD0007-2</c>), because the vendor's ticket has no number of its
/// own and shows the Sales Order's.
///
/// <para><b>A cancellation is a ticket too</b>, carrying negative lines and the reason, so a discarded
/// dish reaches the station that was cooking it. A ticket is all one or all the other, because a send
/// and a discard are separate actions.</para>
/// </summary>
public sealed class KitchenTicket
{
    private readonly List<KitchenTicketLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid PosOrderId { get; private set; }
    public int SendNumber { get; private set; }

    /// <summary>Null is the Default station.</summary>
    public Guid? KitchenStationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    /// <summary>Why a cancellation was made; null exactly when the ticket is a send.</summary>
    public string? Reason { get; private set; }

    /// <summary>Prints recorded so far; the next print is number <c>PrintCount + 1</c>.</summary>
    public int PrintCount { get; private set; }

    /// <summary>Phase 68 -- what the ticket is. Stored, because a move cannot be told from its signs.</summary>
    public KitchenTicketKind Kind { get; private set; }

    /// <summary>Phase 68 -- on a transfer ticket, the other order (where the food went, or came from).</summary>
    public Guid? CounterpartOrderId { get; private set; }

    public IReadOnlyList<KitchenTicketLine> Lines => _lines;

    public bool IsCancellation => Kind == KitchenTicketKind.Cancellation;

    private KitchenTicket()
    {
    }

    internal static KitchenTicket Create(
        Guid orderId,
        int sendNumber,
        Guid? kitchenStationId,
        IReadOnlyList<(Guid LineId, decimal Quantity)> lines,
        Guid userId,
        string? reason,
        DateTimeOffset now,
        KitchenTicketKind kind = KitchenTicketKind.Send,
        Guid? counterpartOrderId = null)
    {
        var ticket = new KitchenTicket
        {
            Id = Guid.NewGuid(),
            PosOrderId = orderId,
            SendNumber = sendNumber,
            KitchenStationId = kitchenStationId,
            CreatedAt = now,
            CreatedByUserId = userId,
            Reason = reason,
            Kind = kind,
            CounterpartOrderId = counterpartOrderId,
        };

        foreach (var (lineId, quantity) in lines)
        {
            ticket._lines.Add(KitchenTicketLine.Create(ticket.Id, lineId, quantity));
        }

        var rows = ticket._lines;
        var valid = kind switch
        {
            KitchenTicketKind.Send => rows.All(x => x.Quantity > 0m) && reason is null && counterpartOrderId is null,
            KitchenTicketKind.Cancellation => rows.All(x => x.Quantity < 0m) && reason is not null && counterpartOrderId is null,
            // A take-away mark moves food between two lines of one order, so it nets to zero.
            KitchenTicketKind.TakeAway => rows.Sum(x => x.Quantity) == 0m && reason is null && counterpartOrderId is null,
            KitchenTicketKind.Transfer => (rows.All(x => x.Quantity > 0m) || rows.All(x => x.Quantity < 0m))
                && reason is null && counterpartOrderId is not null && counterpartOrderId != orderId,
            _ => false,
        };

        if (rows.Count == 0 || !valid)
        {
            throw new InvalidOperationException(
                "A kitchen ticket is a send, a cancellation with its reason, a take-away mark, or a transfer naming the other order.");
        }

        return ticket;
    }

    internal int RecordPrint() => ++PrintCount;
}

/// <summary>One line of a kitchen ticket: the signed change to one order line.</summary>
public sealed class KitchenTicketLine
{
    public Guid Id { get; private set; }
    public Guid KitchenTicketId { get; private set; }
    public Guid PosOrderLineId { get; private set; }

    /// <summary>Positive on a send, negative on a cancellation; never zero.</summary>
    public decimal Quantity { get; private set; }

    private KitchenTicketLine()
    {
    }

    internal static KitchenTicketLine Create(Guid ticketId, Guid lineId, decimal quantity)
    {
        if (quantity == 0m)
        {
            throw new InvalidOperationException("A kitchen ticket line carries a change.");
        }

        return new KitchenTicketLine
        {
            Id = Guid.NewGuid(),
            KitchenTicketId = ticketId,
            PosOrderLineId = lineId,
            Quantity = quantity,
        };
    }
}
