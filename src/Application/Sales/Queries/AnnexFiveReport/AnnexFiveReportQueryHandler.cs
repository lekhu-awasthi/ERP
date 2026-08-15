using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.AnnexFiveReport;

public sealed class AnnexFiveReportQueryHandler(IAppDbContext db) : IRequestHandler<AnnexFiveReportQuery, AnnexFiveReportDto>
{
    public async Task<AnnexFiveReportDto> Handle(AnnexFiveReportQuery request, CancellationToken cancellationToken)
    {
        var invoices = await db.Invoices
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == InvoiceStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId, x.Code, x.Date, x.Status })
            .ToListAsync(cancellationToken);
        var invoiceLines = await db.InvoiceLines
            .Where(x => invoices.Select(i => i.Id).Contains(x.InvoiceId))
            .Select(x => new { x.InvoiceId, x.VatRate, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var creditNotes = await db.CreditNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == CreditNoteStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId, x.Code, x.Date, x.Status })
            .ToListAsync(cancellationToken);
        var creditNoteLines = await db.CreditNoteLines
            .Where(x => creditNotes.Select(c => c.Id).Contains(x.CreditNoteId))
            .Select(x => new { x.CreditNoteId, x.VatRate, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var contactIds = invoices.Select(x => x.ContactId).Concat(creditNotes.Select(x => x.ContactId)).Distinct().ToList();
        var contacts = await db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name, x.Pan })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var rows = new List<AnnexFiveReportRowDto>();

        foreach (var invoice in invoices)
        {
            var lines = invoiceLines.Where(x => x.InvoiceId == invoice.Id).ToList();
            var contact = contacts[invoice.ContactId];
            rows.Add(new AnnexFiveReportRowDto(
                contact.Id, contact.Code, contact.Name, contact.Pan, DocumentType.Invoice,
                invoice.Code, invoice.Date,
                lines.Sum(l => l.Amount),
                lines.Where(l => l.VatRate == VatRate.ThirteenPercentVat).Sum(l => l.Amount),
                lines.Sum(l => l.VatAmount),
                lines.Sum(l => l.Amount + l.VatAmount),
                invoice.Status != InvoiceStatus.Void));
        }

        foreach (var creditNote in creditNotes)
        {
            var lines = creditNoteLines.Where(x => x.CreditNoteId == creditNote.Id).ToList();
            var contact = contacts[creditNote.ContactId];
            rows.Add(new AnnexFiveReportRowDto(
                contact.Id, contact.Code, contact.Name, contact.Pan, DocumentType.CreditNote,
                creditNote.Code, creditNote.Date,
                lines.Sum(l => l.Amount),
                lines.Where(l => l.VatRate == VatRate.ThirteenPercentVat).Sum(l => l.Amount),
                lines.Sum(l => l.VatAmount),
                lines.Sum(l => l.Amount + l.VatAmount),
                creditNote.Status != CreditNoteStatus.Void));
        }

        var orderedRows = rows.OrderBy(x => x.BillDate).ThenBy(x => x.BillNo).ToList();

        return new AnnexFiveReportDto(request.FromDate, request.ToDate, orderedRows);
    }
}
