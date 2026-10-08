using System.Linq.Expressions;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Formatting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales;

/// <summary>
/// Phase 69 -- the tax invoice a credit note relates to (VAT Rules Rule 20(1)(e)), however the note names
/// it: a <b>return</b> through <see cref="CreditNote.ReferrerId"/> (Convert to Credit Note, or a till
/// refund), a <b>price adjustment</b> through <see cref="CreditNote.AgainstInvoiceId"/>, or a typed
/// number and date for an invoice issued before the system (phase-69-status.md Decisions B and C).
///
/// <para>Every reader of "the notes against this invoice" goes through <see cref="RelatesTo"/>, so a
/// price adjustment is counted wherever a return is: the void guard, the value cap and the picker. The
/// per-line quantity caps stay on returns alone (<see cref="SalesValidation.GetInvoiceRemainingByLineAsync"/>):
/// an adjustment gives no quantity back.</para>
/// </summary>
public static class CreditNoteInvoiceReferences
{
    /// <summary>The notes relating to <paramref name="invoiceId"/> either way. An expression rather than a
    /// method called inside one, because a static call inside a LINQ predicate is untranslatable on SQL
    /// Server while InMemory runs it happily (phase 34b).</summary>
    public static Expression<Func<CreditNote, bool>> RelatesTo(Guid invoiceId) =>
        x => (x.ReferrerType == DocumentType.Invoice && x.ReferrerId == invoiceId) || x.AgainstInvoiceId == invoiceId;

    /// <summary>
    /// Names the invoice a standalone note relates to, or clears it, refusing every mismatch with a 409
    /// before the aggregate's own shape check. Call it after the note's customer, date, currency,
    /// location and lines are set: it checks all of them, and the value cap needs the lines.
    /// </summary>
    public static async Task ApplyAsync(
        IAppDbContext db, CreditNote creditNote, Guid? invoiceId, string? invoiceNumber, DateOnly? invoiceDate,
        CancellationToken cancellationToken)
    {
        var number = string.IsNullOrWhiteSpace(invoiceNumber) ? null : invoiceNumber.Trim();

        if (creditNote.IsConversionFromInvoice && (invoiceId is not null || number is not null || invoiceDate is not null))
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(CreditNote.AgainstInvoiceId),
                    "A credit note converted from an invoice already names that invoice.")]);
        }

        if (invoiceId is { } id)
        {
            var invoice = await LoadInvoiceAsync(db, creditNote.OrganizationId, id, cancellationToken);
            EnsureCanBeAdjusted(invoice, creditNote);
        }
        else if (invoiceDate is { } typedDate)
        {
            await EnsureIssuedBeforeTheSystemAsync(db, creditNote.OrganizationId, typedDate, cancellationToken);
        }

        creditNote.SetInvoiceReference(invoiceId, number, invoiceDate);

        if (invoiceId is { } named)
        {
            await EnsureWithinInvoiceValueAsync(db, creditNote, named, cancellationToken);
        }
    }

    /// <summary>
    /// A return (Convert to Credit Note) stays in its invoice's currency, and together with every other
    /// note against the invoice never credits more than the invoice. Called by Create and Update for a
    /// converted note, after its lines are set; the per-line quantity caps are checked separately.
    /// </summary>
    public static async Task EnsureConversionMatchesAsync(
        IAppDbContext db, CreditNote creditNote, CancellationToken cancellationToken)
    {
        if (!creditNote.IsConversionFromInvoice)
        {
            return;
        }

        var invoice = await LoadInvoiceAsync(db, creditNote.OrganizationId, creditNote.ReferrerId!.Value, cancellationToken);
        EnsureSameCurrency(invoice, creditNote);
        await EnsureWithinInvoiceValueAsync(db, creditNote, invoice, cancellationToken);
    }

    /// <summary>
    /// Every non-void note against the invoice -- returns and adjustments, drafts included, this one
    /// excepted -- plus this one, never credits more than the invoice's total or more than its VAT. A
    /// note cannot give back output tax that was never charged. Drafts count, as they do for the
    /// quantity caps, so two drafts cannot each take the whole remainder.
    /// </summary>
    public static async Task EnsureWithinInvoiceValueAsync(
        IAppDbContext db, CreditNote creditNote, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await LoadInvoiceAsync(db, creditNote.OrganizationId, invoiceId, cancellationToken);
        await EnsureWithinInvoiceValueAsync(db, creditNote, invoice, cancellationToken);
    }

    private static async Task EnsureWithinInvoiceValueAsync(
        IAppDbContext db, CreditNote creditNote, Invoice invoice, CancellationToken cancellationToken)
    {
        var (creditedTotal, creditedVat) = await CreditedAsync(db, creditNote.OrganizationId, invoice.Id, creditNote.Id, cancellationToken);

        var invoiceVat = invoice.Lines.Sum(x => x.VatAmount);
        var remainingTotal = invoice.GrandTotal - creditedTotal;
        var remainingVat = invoiceVat - creditedVat;

        if (creditNote.GrandTotal > remainingTotal)
        {
            throw new ConflictException(
                $"This credit note comes to {creditNote.GrandTotal:0.00}, but only {Math.Max(remainingTotal, 0):0.00} of "
                + $"invoice {invoice.Code}'s {invoice.GrandTotal:0.00} is left to credit.");
        }

        var noteVat = creditNote.Lines.Sum(x => x.VatAmount);
        if (noteVat > remainingVat)
        {
            throw new ConflictException(
                $"This credit note gives back VAT of {noteVat:0.00}, but only {Math.Max(remainingVat, 0):0.00} of "
                + $"invoice {invoice.Code}'s VAT is left to credit.");
        }
    }

    /// <summary>What the non-void notes against an invoice already credit, in the invoice's currency:
    /// their grand total and their VAT. The note being edited is left out.</summary>
    public static async Task<(decimal Total, decimal Vat)> CreditedAsync(
        IAppDbContext db, Guid organizationId, Guid invoiceId, Guid? excludingCreditNoteId, CancellationToken cancellationToken)
    {
        var notes = db.CreditNotes
            .Where(x => x.OrganizationId == organizationId && x.Status != CreditNoteStatus.Void)
            .Where(RelatesTo(invoiceId));

        if (excludingCreditNoteId is { } excluded)
        {
            notes = notes.Where(x => x.Id != excluded);
        }

        // Projected to entity columns and summed here: a store-side Sum over a projection is the
        // phase-42 door, and the rows are one invoice's notes.
        var lines = await notes
            .SelectMany(x => x.Lines)
            .Select(x => new { x.Amount, x.ServiceChargeAmount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var roundOffs = await notes.Select(x => x.RoundOff).ToListAsync(cancellationToken);

        return (
            lines.Sum(x => x.Amount + x.ServiceChargeAmount + x.VatAmount) + roundOffs.Sum(),
            lines.Sum(x => x.VatAmount));
    }

    private static async Task<Invoice> LoadInvoiceAsync(
        IAppDbContext db, Guid organizationId, Guid invoiceId, CancellationToken cancellationToken) =>
        await db.Invoices
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == invoiceId && x.OrganizationId == organizationId, cancellationToken)
        ?? throw new NotFoundException("Invoice not found.");

    /// <summary>A price adjustment relates to an issued ERP invoice of the same customer, in its
    /// currency, at its billing location, and cannot precede it.</summary>
    private static void EnsureCanBeAdjusted(Invoice invoice, CreditNote creditNote)
    {
        if (invoice.Status != InvoiceStatus.Approved)
        {
            throw new ConflictException(
                $"Invoice {invoice.Code} is {invoice.Status.ToString().ToLowerInvariant()}; a credit note names an approved invoice.");
        }

        // Phase 63's rule, for the second way a note can reach a till sale: the till is the one door
        // that gives back its service charge and round-off and pays out of a drawer.
        if (invoice.Channel == SalesChannel.Pos)
        {
            throw new ConflictException(
                $"Invoice {invoice.Code} was rung up at a till, so its credit notes are made at the till (Refund).");
        }

        if (invoice.ContactId != creditNote.ContactId)
        {
            throw new ConflictException(
                $"Invoice {invoice.Code} was issued to another customer; a credit note goes to the invoice's own customer.");
        }

        EnsureSameCurrency(invoice, creditNote);

        if (invoice.LocationId != creditNote.LocationId)
        {
            throw new ConflictException(
                $"Invoice {invoice.Code} was issued at another billing location; raise the credit note there.");
        }

        if (creditNote.Date < invoice.Date)
        {
            throw new ConflictException(
                $"This credit note is dated before invoice {invoice.Code} ({RequestCalendar.Format(invoice.Date)}), "
                + "the invoice it relates to.");
        }
    }

    private static void EnsureSameCurrency(Invoice invoice, CreditNote creditNote)
    {
        if (invoice.CurrencyCode != creditNote.CurrencyCode)
        {
            throw new ConflictException(
                $"Invoice {invoice.Code} is in {invoice.CurrencyCode}, so a credit note against it is in "
                + $"{invoice.CurrencyCode} too (this one is in {creditNote.CurrencyCode}).");
        }
    }

    /// <summary>
    /// A typed invoice is one this system has no row for, so it must be older than every invoice the
    /// system has issued (phase-69-status.md Decision C). Otherwise a typed number would be a way round
    /// the picker's checks and the value cap. A tenant that has issued nothing yet may type any date.
    /// </summary>
    private static async Task EnsureIssuedBeforeTheSystemAsync(
        IAppDbContext db, Guid organizationId, DateOnly typedDate, CancellationToken cancellationToken)
    {
        var firstInvoiceDate = await db.Invoices
            .Where(x => x.OrganizationId == organizationId && x.Status != InvoiceStatus.Draft)
            .MinAsync(x => (DateOnly?)x.Date, cancellationToken);

        if (firstInvoiceDate is { } first && typedDate >= first)
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(CreditNote.AgainstInvoiceDate),
                    $"A typed invoice is one issued before you started using this system, so it is dated before your "
                    + $"first invoice here ({RequestCalendar.Format(first)}). Choose a later invoice from the list instead.")]);
        }
    }
}
