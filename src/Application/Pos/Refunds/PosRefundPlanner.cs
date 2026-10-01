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
/// returned, at the sale line's own rate, discount, VAT rate, unit, batch and service charge rate.</item>
/// <item><b>Rounding</b> (F): to the nearest rupee when the location rounds or the sale was rounded,
/// never so that the sale's refunds together exceed the sale; and a refund returning the last of a sale
/// gives back exactly what is left of its total, so a bill refunded in parts ties out to the paisa.</item>
/// <item><b>What is handed back</b> (D): the refund first comes off what the customer still owes on this
/// sale (the one reader of what is owed, phase 36), and the rest is paid out.</item>
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
            [.. chosen.Select(x => new CreditNoteLineInput(
                x.Line.ProductId, x.Quantity, x.Line.Rate, x.Line.VatRate, x.Line.DiscountPct, x.Line.UnitId))],
            cancellationToken);

        var creditNote = CreditNote.CreatePosRefund(
            organizationId, invoice.ContactId, date, till.Location.Id, sessionId, invoice.Id, invoice.Code,
            invoice.DiscountPct, reason);

        foreach (var (line, quantity) in chosen)
        {
            creditNote.AddPosLine(
                line.ProductId, quantity, line.Rate, line.VatRate, line.DiscountPct, line.UnitId,
                line.ConversionFactor, line.BatchId, line.ServiceChargeRate);
        }

        await ApplyRoundOffAsync(db, organizationId, invoice, till, chosen, creditNote, cancellationToken);

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

    /// <summary>
    /// Decision F. The sale's earlier refunds are what makes this the planner's job and not the
    /// aggregate's: whether this refund returns the last of the sale, and how much of the sale's total
    /// is left to give back, are facts about other credit notes.
    /// </summary>
    private static async Task ApplyRoundOffAsync(
        IAppDbContext db, Guid organizationId, Invoice invoice, PosTillContext till,
        List<(InvoiceLine Line, decimal Quantity)> chosen, CreditNote creditNote, CancellationToken cancellationToken)
    {
        var priorNotes = db.CreditNotes.Where(x => x.OrganizationId == organizationId
            && x.ReferrerType == DocumentType.Invoice && x.ReferrerId == invoice.Id
            && x.Status != CreditNoteStatus.Void);

        var priorLines = await (
                from line in db.CreditNoteLines
                join note in priorNotes on line.CreditNoteId equals note.Id
                select line.Amount + line.ServiceChargeAmount + line.VatAmount)
            .SumAsync(cancellationToken);
        var priorTotal = priorLines + await priorNotes.SumAsync(x => x.RoundOff, cancellationToken);

        var leftOfSale = invoice.GrandTotal - priorTotal;
        var unrounded = creditNote.Lines.Sum(x => x.LineTotal);

        var remaining = await SalesValidation.GetInvoiceRemainingByLineAsync(db, organizationId, invoice, cancellationToken);
        var returnedNow = chosen
            .GroupBy(x => (x.Line.ProductId, x.Line.Rate, x.Line.VatRate, x.Line.DiscountPct, x.Line.UnitId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var returnsTheRest = remaining.All(x => x.Value - returnedNow.GetValueOrDefault(x.Key) == 0m);

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
