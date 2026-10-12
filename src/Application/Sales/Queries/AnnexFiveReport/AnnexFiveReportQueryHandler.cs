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
            // Phase 61 -- a till line's service charge is inside the VAT base, so it is taxable with
            // its line. Zero on every ERP line; the round-off is not a supply and is not here.
            // Phase 89 -- Gross is quantity times rate, before any discount, for the Discount column.
            .Select(x => new { x.InvoiceId, x.VatRate, Amount = x.Amount + x.ServiceChargeAmount + x.PackagingFeeAmount, x.VatAmount, Gross = x.Quantity * x.Rate, Net = x.Amount })
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
            // Phase 63 -- a till refund's service charge is inside the taxable value it returns.
            .Select(x => new { x.CreditNoteId, x.VatRate, Amount = x.Amount + x.ServiceChargeAmount + x.PackagingFeeAmount, x.VatAmount, Gross = x.Quantity * x.Rate, Net = x.Amount })
            .ToListAsync(cancellationToken);

        var contactIds = invoices.Select(x => x.ContactId).Concat(creditNotes.Select(x => x.ContactId)).Distinct().ToList();
        var contacts = await db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name, x.Pan })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        // Phase 89 -- the vendor's columns this app can now back. Read for the period's documents, a page of the
        // report's own ids at most per type.
        var invoiceIds = invoices.Select(x => x.Id).ToList();
        var creditNoteIds = creditNotes.Select(x => x.Id).ToList();

        // Phase 67's counted prints: every copy that left, till, PDF or email. The first is the original bill.
        var invoicePrints = (await db.InvoicePrints
                .Where(x => x.OrganizationId == request.OrganizationId && invoiceIds.Contains(x.InvoiceId))
                .Select(x => new { DocumentId = x.InvoiceId, x.PrintNumber, x.PrintedAt, x.PrintedByUserId })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), First: g.OrderBy(x => x.PrintNumber).First()));
        var creditNotePrints = (await db.CreditNotePrints
                .Where(x => x.OrganizationId == request.OrganizationId && creditNoteIds.Contains(x.CreditNoteId))
                .Select(x => new { DocumentId = x.CreditNoteId, x.PrintNumber, x.PrintedAt, x.PrintedByUserId })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), First: g.OrderBy(x => x.PrintNumber).First()));

        // Entered By: the user on the document's Create audit row (AuditBehavior, phase 16d). A document created before
        // the audit trail existed has none, and the cell stays empty rather than naming its approver, who is someone else.
        var documentIds = invoiceIds.Concat(creditNoteIds).ToList();
        var creators = (await db.Audits
                .Where(x => x.OrganizationId == request.OrganizationId && x.Action == "Create"
                    && (x.DocumentType == DocumentType.Invoice || x.DocumentType == DocumentType.CreditNote)
                    && documentIds.Contains(x.DocumentId))
                .Select(x => new { x.DocumentId, x.UserId, x.CreatedAt })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreatedAt).First().UserId);

        var userIds = creators.Values
            .Concat(invoicePrints.Values.Select(x => x.First.PrintedByUserId))
            .Concat(creditNotePrints.Values.Select(x => x.First.PrintedByUserId))
            .Distinct()
            .ToList();
        var userNames = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        AnnexFiveReportRowDto Row(
            Guid id, Guid contactId, DocumentType type, string code, DateOnly date, bool active,
            IReadOnlyCollection<(VatRate VatRate, decimal Amount, decimal VatAmount, decimal Gross, decimal Net)> lines,
            (int Count, DateTimeOffset PrintedAt, Guid PrintedByUserId)? prints)
        {
            var contact = contacts[contactId];
            var discount = lines.Sum(l => l.Gross - l.Net);
            var afterDiscount = lines.Sum(l => l.Amount);
            return new AnnexFiveReportRowDto(
                contact.Id, contact.Code, contact.Name, contact.Pan, type, code, date,
                afterDiscount + discount,
                lines.Where(l => l.VatRate == VatRate.ThirteenPercentVat).Sum(l => l.Amount),
                lines.Sum(l => l.VatAmount),
                lines.Sum(l => l.Amount + l.VatAmount),
                active,
                BsCalendar.FiscalYearOf(date),
                discount,
                prints?.Count ?? 0,
                prints?.PrintedAt,
                prints is { } p ? userNames.GetValueOrDefault(p.PrintedByUserId) : null,
                creators.TryGetValue(id, out var creator) ? userNames.GetValueOrDefault(creator) : null);
        }

        var invoiceLinesById = invoiceLines.GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g => g.ToList());
        var creditNoteLinesById = creditNoteLines.GroupBy(x => x.CreditNoteId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<AnnexFiveReportRowDto>();

        foreach (var invoice in invoices)
        {
            var lines = invoiceLinesById.GetValueOrDefault(invoice.Id) ?? [];
            rows.Add(Row(
                invoice.Id, invoice.ContactId, DocumentType.Invoice, invoice.Code, invoice.Date,
                invoice.Status != InvoiceStatus.Void,
                [.. lines.Select(l => (l.VatRate, l.Amount, l.VatAmount, l.Gross, l.Net))],
                invoicePrints.TryGetValue(invoice.Id, out var p) ? (p.Count, p.First.PrintedAt, p.First.PrintedByUserId) : null));
        }

        foreach (var creditNote in creditNotes)
        {
            var lines = creditNoteLinesById.GetValueOrDefault(creditNote.Id) ?? [];
            rows.Add(Row(
                creditNote.Id, creditNote.ContactId, DocumentType.CreditNote, creditNote.Code, creditNote.Date,
                creditNote.Status != CreditNoteStatus.Void,
                [.. lines.Select(l => (l.VatRate, l.Amount, l.VatAmount, l.Gross, l.Net))],
                creditNotePrints.TryGetValue(creditNote.Id, out var p) ? (p.Count, p.First.PrintedAt, p.First.PrintedByUserId) : null));
        }

        var orderedRows = rows.OrderBy(x => x.BillDate).ThenBy(x => x.BillNo).ToList();
        var paged = request.ExportAll
            ? orderedRows.ToUnpagedResult()
            : orderedRows.ToPagedResult(request.Page, request.PageSize);

        return new AnnexFiveReportDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount);
    }
}
