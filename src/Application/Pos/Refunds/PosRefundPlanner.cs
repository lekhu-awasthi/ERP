using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Queries.Ageing;
using ErpApp.Application.Pos.Commands.CreatePosRefund;
using ErpApp.Application.Sales;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Refunds;

/// <summary>A refund planned but not yet paid out or saved.</summary>
/// <param name="CreditNote">The note, built in memory with its lines and round-off. Not added to the
/// context: the preview discards it, and the command adds it.</param>
/// <param name="RequiredPayout">What must be handed back; the rest comes off what the customer owes.</param>
/// <param name="OwedBefore">What the customer still owed on the sale before this refund.</param>
internal sealed record PosRefundPlan(Invoice Invoice, CreditNote CreditNote, decimal RequiredPayout, decimal OwedBefore);

/// <summary>
/// Phase 63 -- everything a refund decides before money moves, shared by the till's preview
/// (<c>PreviewPosRefundQuery</c>) and the refund itself (<c>CreatePosRefundCommand</c>), so the figure
/// the cashier is shown and the figure posted are one computation (phase 38's rule for a dry run: it
/// shares every line of the resolution with the real run). The till keeps no copy of this arithmetic:
/// unlike a sale's total (phase 62 Decision E), a refund's depends on other documents -- earlier
/// refunds of the sale and what the customer still owes -- which only the server can see.
///
/// <para>Each rule is a decision in phase-63-status.md:</para>
/// <list type="bullet">
/// <item><b>Which sale</b> (C): an approved till sale of this location.</item>
/// <item><b>How much</b> (phase 6's cap): each line at most what was sold less what earlier credit notes
/// returned, at the sale line's own rate, discount, VAT rate, unit, batch, service charge rate and (phase 70)
/// packaging fee terms.</item>
/// <item><b>Rounding</b> (F): to the nearest rupee when the location rounds or the sale was rounded,
/// never so that the sale's refunds together exceed the sale; and a refund returning the last of a sale
/// gives back exactly what is left of its total, so a bill refunded in parts ties out to the paisa.</item>
/// <item><b>What is handed back</b> (D): the refund first comes off what the customer still owes on this
/// sale (the one reader of what is owed, phase 36), and the rest is paid out.</item>
/// <item><b>Phase 74 -- the last of a line takes what is left of it.</b> A refund that returns the last units
/// of a sale's (product, rate, VAT, discount, unit) key gives back exactly what the sale charged for them, less
/// what earlier refunds gave back: amount, service charge, packaging fee, VAT and share of the bill's Amount
/// discount, each. Priced afresh, the parts' rounding could leave a paisa of VAT in the VAT account on a fully
/// returned sale (phase 72's open item); the vendor's left 2.08, by taking its bill discount off twice (read live
/// 2026-10-10). Any other refund line is priced afresh, with its quantity's share of the bill discount.</item>
/// </list>
/// </summary>
internal static class PosRefundPlanner
{
    public static async Task<PosRefundPlan> PlanAsync(
        IAppDbContext db,
        Guid organizationId,
        PosTillContext till,
        Guid sessionId,
        Guid invoiceId,
        IReadOnlyList<PosRefundLineInput> requestedLines,
        string reason,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == invoiceId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Sale not found.");

        EnsureRefundable(invoice, till);

        var chosen = ResolveLines(requestedLines, invoice);

        // Phase 6's cap, by the same (product, rate, VAT, discount, unit) key every credit note against
        // this invoice is counted under -- including any raised from the ERP before this phase.
        await SalesValidation.EnsureCreditNoteLinesWithinInvoiceRemainingAsync(
            db, organizationId, invoice.Id, invoice.ContactId, invoice.DiscountPct,
            [.. chosen.Select(x => (SaleLineKey.Of(x.Line), x.Quantity))],
            cancellationToken);

        var creditNote = CreditNote.CreatePosRefund(
            organizationId, invoice.ContactId, date, till.Location.Id, sessionId, invoice.Id, invoice.Code,
            invoice.DiscountPct, reason);

        var priorNotes = await PriorRefundsQuery(db, organizationId, invoice)
            .AsNoTracking()
            .Include(x => x.Lines)
            .ToListAsync(cancellationToken);
        var priorLines = priorNotes.SelectMany(x => x.Lines).ToList();
        var remaining = await SalesValidation.GetInvoiceRemainingByLineAsync(db, organizationId, invoice, cancellationToken);
        var lastOfKey = chosen
            .GroupBy(x => SaleLineKey.Of(x.Line))
            .Where(g => remaining.GetValueOrDefault(g.Key) - g.Sum(x => x.Quantity) == 0m)
            .ToDictionary(g => g.Key, g => g.Last().Line.Id);

        foreach (var (line, quantity) in chosen)
        {
            var key = SaleLineKey.Of(line);
            PosLineArithmetic.Figures? theRest = null;
            var share = PosLineArithmetic.Prorate(line.BillDiscountAmount, quantity, line.Quantity);

            if (lastOfKey.GetValueOrDefault(key) == line.Id)
            {
                var sold = invoice.Lines.Where(x => SaleLineKey.Of(x) == key).ToList();
                var givenBack = priorLines.Where(x => SaleLineKey.Of(x) == key)
                    .Concat(creditNote.Lines.Where(x => SaleLineKey.Of(x) == key))
                    .ToList();

                theRest = Sum(sold.Select(x => x.Figures)) - Sum(givenBack.Select(x => x.Figures));
                share = sold.Sum(x => x.BillDiscountAmount) - givenBack.Sum(x => x.BillDiscountAmount);
            }

            creditNote.AddPosLine(new CreditNote.PosLineInput(
                line.ProductId, quantity, line.Rate, line.VatRate, line.DiscountPerUnit, line.DiscountPct, share,
                line.UnitId, line.ConversionFactor, line.BatchId, line.ServiceChargeRate, line.PackagingFee, theRest));
        }

        ApplyRoundOff(invoice, till, priorNotes, lastOfKey.Count == remaining.Count(x => x.Value > 0m), creditNote);

        var owed = await OwedOnSaleAsync(db, organizationId, invoice, date, cancellationToken);
        var requiredPayout = creditNote.GrandTotal - Math.Min(creditNote.GrandTotal, owed);

        return new PosRefundPlan(invoice, creditNote, requiredPayout, owed);
    }

    /// <summary>What the customer still owes on <paramref name="invoice"/>, never below zero. A till sale
    /// is always base currency, so the reader's base figure is the sale's own.</summary>
    public static async Task<decimal> OwedOnSaleAsync(
        IAppDbContext db, Guid organizationId, Invoice invoice, DateOnly asOf, CancellationToken cancellationToken)
    {
        var outstanding = await OutstandingDocumentReader.LoadAsync(
            db, organizationId, ContactType.Customer, asOf, cancellationToken, invoice.ContactId);

        var owed = outstanding
            .Where(x => x.Type == AgeableDocumentType.Invoice && x.Id == invoice.Id)
            .Sum(x => x.Balance);

        return Math.Max(owed, 0m);
    }

    public static void EnsureRefundable(Invoice invoice, PosTillContext till)
    {
        if (invoice.Channel != SalesChannel.Pos)
        {
            throw new ConflictException(
                $"Invoice {invoice.Code} was not rung up at a till. Return it with a credit note from the invoice page.");
        }

        if (invoice.Status != InvoiceStatus.Approved)
        {
            throw new ConflictException($"Invoice {invoice.Code} has been voided, so there is nothing to refund.");
        }

        if (invoice.LocationId != till.Location.Id)
        {
            throw new ConflictException(
                $"Invoice {invoice.Code} was sold at another location. A sale is refunded at a till of the location that sold it.");
        }
    }

    private static List<(InvoiceLine Line, decimal Quantity)> ResolveLines(
        IReadOnlyList<PosRefundLineInput> requested, Invoice invoice)
    {
        var byId = invoice.Lines.ToDictionary(x => x.Id);

        var unknown = requested.Count(x => !byId.ContainsKey(x.InvoiceLineId));
        if (unknown > 0)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreatePosRefundCommand.Lines), $"{unknown} line(s) named here are not lines of invoice {invoice.Code}.")]);
        }

        // In the sale's own line order, so the note reads like the bill it returns.
        var quantities = requested.ToDictionary(x => x.InvoiceLineId, x => x.Quantity);
        return [.. invoice.Lines.Where(x => quantities.ContainsKey(x.Id)).Select(x => (x, quantities[x.Id]))];
    }

    private static IQueryable<CreditNote> PriorRefundsQuery(IAppDbContext db, Guid organizationId, Invoice invoice) =>
        db.CreditNotes.Where(x => x.OrganizationId == organizationId
            && x.ReferrerType == DocumentType.Invoice && x.ReferrerId == invoice.Id
            && x.Status != CreditNoteStatus.Void);

    private static PosLineArithmetic.Figures Sum(IEnumerable<PosLineArithmetic.Figures> figures) =>
        figures.Aggregate(PosLineArithmetic.Figures.Zero, (sum, x) => sum + x);

    /// <summary>
    /// Decision F. The sale's earlier refunds are what makes this the planner's job and not the
    /// aggregate's: whether this refund returns the last of the sale, and how much of the sale's total
    /// is left to give back, are facts about other credit notes.
    /// </summary>
    private static void ApplyRoundOff(
        Invoice invoice, PosTillContext till, IReadOnlyList<CreditNote> priorNotes, bool returnsTheRest,
        CreditNote creditNote)
    {
        var priorTotal = priorNotes.Sum(x => x.Lines.Sum(l => l.LineTotal) + x.RoundOff);

        var leftOfSale = invoice.GrandTotal - priorTotal;
        var unrounded = creditNote.Lines.Sum(x => x.LineTotal);

        decimal total;
        if (returnsTheRest)
        {
            total = leftOfSale;
        }
        else if (till.Settings.RoundOffEnabled || invoice.RoundOff != 0m)
        {
            total = Math.Min(decimal.Round(unrounded, 0, MidpointRounding.AwayFromZero), leftOfSale);
        }
        else
        {
            total = Math.Min(unrounded, leftOfSale);
        }

        if (total != unrounded)
        {
            creditNote.SetRoundOff(total - unrounded);
        }
    }
}
