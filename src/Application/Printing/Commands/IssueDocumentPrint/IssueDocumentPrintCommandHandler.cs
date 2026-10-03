using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Printing.Commands.IssueDocumentPrint;

/// <summary>
/// Writes the print row, then renders through <see cref="PrintDocumentQuery"/> with that row as its
/// <see cref="PrintDocumentQuery.Issue"/>, so the counted PDF is the ordinary print plus a heading and a
/// copy mark -- never a second layout.
///
/// <para><b>The row is committed before anything is rendered</b>, the claim-then-act order every
/// exactly-once write here follows: the number printed on the paper is the number the database
/// accepted. A rendering that fails afterwards leaves a counted request with no paper, which errs the
/// safe way (phase 62: counted on request, so the next print says "copy", never a second
/// original).</para>
///
/// <para><b>A lost race detaches its row.</b> The email job shares this scope's change tracker with
/// its own <c>EmailSendLog</c>, and a print row left tracked after the unique index refused it would
/// fail the job's next <c>SaveChangesAsync</c> as well, recording the send as failed for the wrong
/// reason.</para>
/// </summary>
public sealed class IssueDocumentPrintCommandHandler(IAppDbContext db, ICurrentUserService currentUser, ISender sender)
    : IRequestHandler<IssueDocumentPrintCommand, PrintableDocumentDto>
{
    public async Task<PrintableDocumentDto> Handle(IssueDocumentPrintCommand request, CancellationToken cancellationToken)
    {
        var issue = request.DocumentType switch
        {
            DocumentType.Invoice => await RecordInvoicePrintAsync(request, cancellationToken),
            DocumentType.CreditNote => await RecordCreditNotePrintAsync(request, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request.DocumentType), request.DocumentType, "Only an invoice or a credit note is a counted print."),
        };

        return await sender.Send(
            new PrintDocumentQuery(request.OrganizationId, request.DocumentType, request.DocumentId, issue),
            cancellationToken);
    }

    private async Task<DocumentPrintIssue> RecordInvoicePrintAsync(IssueDocumentPrintCommand request, CancellationToken ct)
    {
        var invoice = await db.Invoices
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Invoice not found.");

        // Asked here first so each refusal is a 409 naming the bill; the Domain refuses both as well.
        RefuseUnlessApproved(invoice.Status == InvoiceStatus.Void, invoice.Status != InvoiceStatus.Approved, "Invoice", invoice.Code);

        var printsSoFar = await db.InvoicePrints.CountAsync(
            x => x.OrganizationId == request.OrganizationId && x.InvoiceId == invoice.Id, ct);

        var print = InvoicePrint.Record(invoice, printsSoFar, currentUser.UserId, DateTimeOffset.UtcNow, request.Medium);
        db.InvoicePrints.Add(print);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.InvoicePrints.Entry(print).State = EntityState.Detached;
            throw RaceLost("Invoice", invoice.Code);
        }

        return new DocumentPrintIssue(print.PrintNumber, print.PrintedByUserId, print.PrintedAt);
    }

    private async Task<DocumentPrintIssue> RecordCreditNotePrintAsync(IssueDocumentPrintCommand request, CancellationToken ct)
    {
        var note = await db.CreditNotes
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Credit note not found.");

        RefuseUnlessApproved(note.Status == CreditNoteStatus.Void, note.Status != CreditNoteStatus.Approved, "Credit note", note.Code);

        var printsSoFar = await db.CreditNotePrints.CountAsync(
            x => x.OrganizationId == request.OrganizationId && x.CreditNoteId == note.Id, ct);

        var print = CreditNotePrint.Record(note, printsSoFar, currentUser.UserId, DateTimeOffset.UtcNow, request.Medium);
        db.CreditNotePrints.Add(print);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.CreditNotePrints.Entry(print).State = EntityState.Detached;
            throw RaceLost("Credit note", note.Code);
        }

        return new DocumentPrintIssue(print.PrintNumber, print.PrintedByUserId, print.PrintedAt);
    }

    /// <summary>Decision E: a draft has no number until it is approved, and a voided document is no
    /// longer one to hand anyone; the till refuses both for the same reasons.</summary>
    private static void RefuseUnlessApproved(bool isVoid, bool isNotApproved, string label, string code)
    {
        if (isVoid)
        {
            throw new ConflictException($"{label} {code} has been voided, so it is no longer one to hand anyone.");
        }

        if (isNotApproved)
        {
            throw new ConflictException($"{label} is a draft. It has no number until it is approved, so it cannot be printed.");
        }
    }

    private static ConflictException RaceLost(string label, string code) =>
        new($"{label} {code} was printed somewhere else at the same moment. Print it again.");
}
