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
    string? Note,
    Guid? KitchenStationId);

/// <summary>A quantity against one existing line: more of it (a send), served, or discarded.</summary>
public sealed record PosOrderLineQuantity(Guid LineId, decimal Quantity);

/// <summary>What a send created, for the caller to add through its own sets (phase 24).</summary>
public sealed record PosOrderSendResult(IReadOnlyList<PosOrderLine> NewLines, IReadOnlyList<KitchenTicket> NewTickets);

/// <summary>A line's quantities, every one but <see cref="Served"/> a sum over its kitchen ticket lines.</summary>
public sealed record PosOrderLineQuantities(decimal Ordered, decimal Discarded, decimal Net, decimal Served)
{
    /// <summary>What the kitchen still owes the table.</summary>
    public decimal Outstanding => Net - Served;
}

/// <summary>
/// Phase 65 -- one send ticket's line on the kitchen board: what it asked for, and how much of that has
/// since been served, cancelled, or is still to cook. Derived, never stored (see
/// <see cref="PosOrder.TicketProgress"/>).
/// </summary>
public sealed record KitchenTicketLineProgress(Guid OrderLineId, decimal Sent, decimal Served, decimal Cancelled)
{
    public decimal Pending => Sent - Served - Cancelled;
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

        var tickets = Ticket(changes, userId, reason: null);
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
    /// </summary>
    public IReadOnlyList<KitchenTicket> Discard(
        IReadOnlyList<PosOrderLineQuantity> items, string reason, Guid userId,
        IReadOnlyDictionary<Guid, decimal> invoiced)
    {
        EnsureOpen();
        EnsureNotEmpty(items, "Choose what to discard.");
        EnsureDistinct(items);

        var changes = new List<(PosOrderLine Line, decimal Quantity)>();

        foreach (var item in items)
        {
            var line = FindLine(item.LineId);
            var quantity = RequireQuantity(item.Quantity);
            var net = QuantitiesOf(line).Net;
            var billed = invoiced.GetValueOrDefault(line.Id);

            if (quantity > net - billed)
            {
                throw new InvalidOperationException(billed > 0m
                    ? $"Line {line.LineNo} has {net:0.####} on the order and {billed:0.####} of it billed, so at most "
                      + $"{net - billed:0.####} can be discarded. What is billed is refunded, not discarded."
                    : $"Line {line.LineNo} has {net:0.####} on the order, so {quantity:0.####} cannot be discarded.");
            }

            changes.Add((line, -quantity));
        }

        return Ticket(changes, userId, RequireReason(reason));
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

        var tickets = changes.Count == 0 ? [] : Ticket(changes, userId, why);

        Status = PosOrderStatus.Voided;
        VoidReason = why;
        VoidedAt = now;
        VoidedByUserId = userId;
        Touch();

        return tickets;
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
        decimal ordered = 0m, discarded = 0m;

        foreach (var ticket in _tickets)
        {
            foreach (var row in ticket.Lines)
            {
                if (row.PosOrderLineId != line.Id)
                {
                    continue;
                }

                if (row.Quantity > 0m)
                {
                    ordered += row.Quantity;
                }
                else
                {
                    discarded -= row.Quantity;
                }
            }
        }

        return new PosOrderLineQuantities(ordered, discarded, ordered - discarded, line.ServedQuantity);
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
        var sends = _tickets
            .Where(x => !x.IsCancellation)
            .OrderBy(x => x.SendNumber)
            .ThenBy(x => x.CreatedAt)
            .ToList();

        var allocated = new Dictionary<(Guid TicketId, Guid LineId), (decimal Served, decimal Cancelled)>();

        foreach (var line in _lines)
        {
            var rows = sends
                .SelectMany(t => t.Lines.Where(r => r.PosOrderLineId == line.Id).Select(r => (Ticket: t, Row: r)))
                .ToList();

            var q = QuantitiesOf(line);
            var served = q.Served;
            var cancelled = q.Discarded;
            var cut = new Dictionary<Guid, (decimal Served, decimal Cancelled)>();

            foreach (var (ticket, row) in rows)
            {
                var take = Math.Min(row.Quantity, served);
                served -= take;
                cut[ticket.Id] = (take, 0m);
            }

            for (var i = rows.Count - 1; i >= 0; i--)
            {
                var (ticket, row) = rows[i];
                var (s, _) = cut[ticket.Id];
                var take = Math.Min(row.Quantity - s, cancelled);
                cancelled -= take;
                cut[ticket.Id] = (s, take);
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
                if (ticket.IsCancellation)
                {
                    return new KitchenTicketProgress(
                        ticket.Id, KitchenTicketState.Cancellation,
                        [.. ticket.Lines.Select(r => new KitchenTicketLineProgress(r.PosOrderLineId, r.Quantity, 0m, 0m))]);
                }

                var lines = ticket.Lines
                    .Select(r =>
                    {
                        var (s, c) = allocated.GetValueOrDefault((ticket.Id, r.PosOrderLineId));
                        return new KitchenTicketLineProgress(r.PosOrderLineId, r.Quantity, s, c);
                    })
                    .ToList();

                var state = lines.Any(x => x.Pending > 0m) ? KitchenTicketState.Pending
                    : lines.All(x => x.Served == 0m) ? KitchenTicketState.Cancelled
                    : KitchenTicketState.Served;

                return new KitchenTicketProgress(ticket.Id, state, lines);
            })
            .ToList();
    }

    /// <summary>The order's running figures before any bill: each line's net quantity priced by phase
    /// 63's <see cref="PosLineArithmetic"/>. Unrounded to the rupee -- the bill (phase 65) rounds.</summary>
    public PosLineArithmetic.Figures Estimate()
    {
        decimal amount = 0m, serviceCharge = 0m, vat = 0m;

        foreach (var line in _lines)
        {
            var figures = line.Figures(QuantitiesOf(line).Net);
            amount += figures.Amount;
            serviceCharge += figures.ServiceChargeAmount;
            vat += figures.VatAmount;
        }

        return new PosLineArithmetic.Figures(amount, serviceCharge, vat);
    }

    public static string Describe(PosTab orderType) => orderType switch
    {
        PosTab.DineIn => "Dine In",
        PosTab.TakeAway => "Take Away",
        PosTab.Delivery => "Delivery",
        _ => orderType.ToString(),
    };

    private List<KitchenTicket> Ticket(List<(PosOrderLine Line, decimal Quantity)> changes, Guid userId, string? reason)
    {
        var sendNumber = _tickets.Count == 0 ? 1 : _tickets.Max(x => x.SendNumber) + 1;
        var now = DateTimeOffset.UtcNow;

        var tickets = changes
            .GroupBy(x => x.Line.KitchenStationId)
            .OrderBy(g => g.Key is null ? 1 : 0)
            .Select(g => KitchenTicket.Create(
                Id, sendNumber, g.Key, [.. g.Select(x => (x.Line.Id, x.Quantity))], userId, reason, now))
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
/// handler prices the line from the product, so a tab cannot be cheapened at the table. A discount is
/// the bill's business (phase 65).</para>
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

    public string? Note { get; private set; }

    /// <summary>Where this line's tickets go: the product's station when first sent; null is Default.</summary>
    public Guid? KitchenStationId { get; private set; }

    /// <summary>How much has left the kitchen for the table. The one quantity stored on a line.</summary>
    public decimal ServedQuantity { get; private set; }

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
            Note = note,
            KitchenStationId = row.KitchenStationId,
        };
    }

    /// <summary>This line's money at <paramref name="quantity"/>, by the till's one arithmetic.</summary>
    public PosLineArithmetic.Figures Figures(decimal quantity) =>
        PosLineArithmetic.Compute(quantity, Rate, VatRate.ToPercent(), 0m, 0m, ServiceChargeRate);

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

    public IReadOnlyList<KitchenTicketLine> Lines => _lines;

    public bool IsCancellation => _lines.Count > 0 && _lines.All(x => x.Quantity < 0m);

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
        DateTimeOffset now)
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
        };

        foreach (var (lineId, quantity) in lines)
        {
            ticket._lines.Add(KitchenTicketLine.Create(ticket.Id, lineId, quantity));
        }

        if (ticket.IsCancellation != (reason is not null))
        {
            throw new InvalidOperationException("A kitchen ticket is a send or a cancellation with its reason, not both.");
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
