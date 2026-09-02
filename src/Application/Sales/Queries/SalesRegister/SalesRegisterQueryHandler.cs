using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.SalesRegister;

public sealed class SalesRegisterQueryHandler(IAppDbContext db) : IRequestHandler<SalesRegisterQuery, SalesRegisterDto>
{
    public async Task<SalesRegisterDto> Handle(SalesRegisterQuery request, CancellationToken cancellationToken)
    {
        var taggedInvoiceIds = await ReportingTagFilter.ResolveMatchingDocumentIdsAsync(
            db, DocumentType.Invoice, request.TagOptionIds, cancellationToken);
        var tagFilterActive = request.TagOptionIds is { Count: > 0 };

        var rows = new List<SalesRegisterRowDto>();

        if (!tagFilterActive || taggedInvoiceIds is { Count: > 0 })
        {
            var invoiceQuery = db.Invoices.Where(x =>
                x.OrganizationId == request.OrganizationId && x.Status == InvoiceStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate);
            if (request.ContactId is { } invoiceContactId)
            {
                invoiceQuery = invoiceQuery.Where(x => x.ContactId == invoiceContactId);
            }
            if (taggedInvoiceIds is not null)
            {
                invoiceQuery = invoiceQuery.Where(x => taggedInvoiceIds.Contains(x.Id));
            }

            var invoices = await invoiceQuery
                .Select(x => new
                {
                    x.Id, x.ContactId, x.Code, x.Date,
                    x.IsExport, x.ExportCountry, x.ExportDeclarationNo, x.ExportDeclarationDate,
                })
                .ToListAsync(cancellationToken);
            var invoiceIds = invoices.Select(x => x.Id).ToList();
            var invoiceLines = await db.InvoiceLines
                .Where(x => invoiceIds.Contains(x.InvoiceId))
                .Select(x => new { x.InvoiceId, x.Amount, x.VatAmount })
                .ToListAsync(cancellationToken);
            var invoiceTotals = invoiceLines.GroupBy(x => x.InvoiceId)
                .ToDictionary(g => g.Key, g => (
                    Total: g.Sum(x => x.Amount + x.VatAmount),
                    TaxExempt: g.Where(x => x.VatAmount == 0).Sum(x => x.Amount),
                    Taxable: g.Where(x => x.VatAmount != 0).Sum(x => x.Amount),
                    Vat: g.Sum(x => x.VatAmount)));

            var invoiceContactIds = invoices.Select(x => x.ContactId).Distinct().ToList();
            var invoiceContacts = await db.Contacts
                .Where(x => invoiceContactIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name, x.Pan })
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            rows.AddRange(invoices.Select(x =>
            {
                var totals = invoiceTotals.GetValueOrDefault(x.Id);
                var contact = invoiceContacts[x.ContactId];
                // FR-5.8 (Phase 23). These four columns existed from Phase 19 and were hardcoded to
                // zero/null because Invoice had no export flag until now.
                //
                // ExportValue is the invoice's own total, reported in its own statutory column --
                // and deliberately NOT added to TaxableValue. An export sale is zero-rated, so its
                // lines are ZeroVat (Invoice.AddLine enforces that), which means the existing
                // "VatAmount == 0 => tax-exempt" split already keeps it out of Taxable. Stating it
                // here because the alternative -- letting an export sale inflate Taxable Sales --
                // is exactly the kind of wrong-column error Phase 6's bug #3 is the reminder for.
                return new SalesRegisterRowDto(
                    x.Date, DocumentType.Invoice, x.Code, x.ContactId, contact.Name, contact.Pan,
                    totals.Total, totals.TaxExempt, totals.Taxable, totals.Vat,
                    ExportValue: x.IsExport ? totals.Total : 0,
                    ExportCountry: x.IsExport ? x.ExportCountry : null,
                    ExportDeclarationNo: x.IsExport ? x.ExportDeclarationNo : null,
                    ExportDeclarationDate: x.IsExport ? x.ExportDeclarationDate : null);
            }));
        }

        if (!tagFilterActive)
        {
            var creditNoteQuery = db.CreditNotes.Where(x =>
                x.OrganizationId == request.OrganizationId && x.Status == CreditNoteStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate);
            if (request.ContactId is { } creditNoteContactId)
            {
                creditNoteQuery = creditNoteQuery.Where(x => x.ContactId == creditNoteContactId);
            }

            var creditNotes = await creditNoteQuery
                .Select(x => new { x.Id, x.ContactId, x.Code, x.Date })
                .ToListAsync(cancellationToken);
            var creditNoteIds = creditNotes.Select(x => x.Id).ToList();
            var creditNoteLines = await db.CreditNoteLines
                .Where(x => creditNoteIds.Contains(x.CreditNoteId))
                .Select(x => new { x.CreditNoteId, x.Amount, x.VatAmount })
                .ToListAsync(cancellationToken);
            var creditNoteTotals = creditNoteLines.GroupBy(x => x.CreditNoteId)
                .ToDictionary(g => g.Key, g => (
                    Total: g.Sum(x => x.Amount + x.VatAmount),
                    TaxExempt: g.Where(x => x.VatAmount == 0).Sum(x => x.Amount),
                    Taxable: g.Where(x => x.VatAmount != 0).Sum(x => x.Amount),
                    Vat: g.Sum(x => x.VatAmount)));

            var creditNoteContactIds = creditNotes.Select(x => x.ContactId).Distinct().ToList();
            var creditNoteContacts = await db.Contacts
                .Where(x => creditNoteContactIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name, x.Pan })
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            rows.AddRange(creditNotes.Select(x =>
            {
                var totals = creditNoteTotals.GetValueOrDefault(x.Id);
                var contact = creditNoteContacts[x.ContactId];
                // A CreditNote carries no export block of its own -- there is no export flag on the
                // aggregate and the live reference product does not offer one -- so these stay empty
                // here rather than being derived from the Invoice it reverses.
                return new SalesRegisterRowDto(
                    x.Date, DocumentType.CreditNote, x.Code, x.ContactId, contact.Name, contact.Pan,
                    -totals.Total, -totals.TaxExempt, -totals.Taxable, -totals.Vat,
                    ExportValue: 0, ExportCountry: null, ExportDeclarationNo: null, ExportDeclarationDate: null);
            }));
        }

        var orderedRows = rows.OrderBy(x => x.Date).ThenBy(x => x.DocumentCode).ToList();
        var paged = request.ExportAll ? orderedRows.ToUnpagedResult() : orderedRows.ToPagedResult(request.Page, request.PageSize);

        return new SalesRegisterDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            orderedRows.Sum(x => x.TotalValue), orderedRows.Sum(x => x.TaxExemptValue),
            orderedRows.Sum(x => x.TaxableValue), orderedRows.Sum(x => x.VatAmount));
    }
}
