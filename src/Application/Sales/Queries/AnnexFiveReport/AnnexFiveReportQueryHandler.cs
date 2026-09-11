using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.AnnexFiveReport;

public sealed class AnnexFiveReportQueryHandler(IAppDbContext db, ICurrentUserService currentUser) : IRequestHandler<AnnexFiveReportQuery, AnnexFiveReportDto>
{
    public async Task<AnnexFiveReportDto> Handle(AnnexFiveReportQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null (unrestricted) unless the tenant has turned
        // the toggle on AND this caller's role carries location-specific grants, so no existing
        // tenant's figures change. Narrows rows in addition to request.LocationId, which is the
        // user's own filter -- two mechanisms, two reasons, both applied.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        // Phase 16a: Approved-only used to be the entire filter, which meant IsActive below could
        // never actually be false -- a Void row never even reached this query. Now both statuses
        // are pulled so a voided Invoice/CreditNote still appears on the register with
        // IsActive=false, matching this report's own "flat bill audit log" shape (phase-8f-status.md).
        var invoices = await db.Invoices
            .Where(x => x.OrganizationId == request.OrganizationId
                && (x.Status == InvoiceStatus.Approved || x.Status == InvoiceStatus.Void)
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .AtLocations(request.LocationId, reportLocations)
            .Select(x => new { x.Id, x.ContactId, x.Code, x.Date, x.Status })
            .ToListAsync(cancellationToken);
        var invoiceLines = await db.InvoiceLines
            .Where(x => invoices.Select(i => i.Id).Contains(x.InvoiceId))
            .Select(x => new { x.InvoiceId, x.VatRate, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var creditNotes = await db.CreditNotes
            .Where(x => x.OrganizationId == request.OrganizationId
                && (x.Status == CreditNoteStatus.Approved || x.Status == CreditNoteStatus.Void)
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .AtLocations(request.LocationId, reportLocations)
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
        var paged = request.ExportAll
            ? orderedRows.ToUnpagedResult()
            : orderedRows.ToPagedResult(request.Page, request.PageSize);

        return new AnnexFiveReportDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount);
    }
}
