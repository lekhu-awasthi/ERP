using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.GetInvoice;

public sealed class GetInvoiceQueryHandler(IAppDbContext db) : IRequestHandler<GetInvoiceQuery, InvoiceDetailDto>
{
    public async Task<InvoiceDetailDto> Handle(GetInvoiceQuery request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");

        IReadOnlyList<PostedGlLineDto>? glLines = null;

        if (invoice.Status == InvoiceStatus.Approved)
        {
            // Phase 37 -- every entry this document posted, not "the" entry (phase 36's
            // finding, generalised): a cost catch-up rides on the same
            // (SourceDocumentType, SourceDocumentId) pair, and SingleOrDefaultAsync throws on
            // two rows exactly as SingleAsync does. The panel shows what the document really
            // did to the ledger, so it shows all of it.
            var glEntries = await SourceDocumentGlEntries.LoadAsync(
                db, DocumentType.Invoice, invoice.Id, cancellationToken);

            glLines = glEntries.Count == 0
                ? null
                : glEntries.SelectMany(e => e.Lines)
                    .Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
        }

        return new InvoiceDetailDto(
            invoice.Id,
            invoice.OrganizationId,
            invoice.ContactId,
            invoice.WarehouseId,
            invoice.LocationId,
            invoice.Code,
            invoice.Date,
            invoice.DueDate,
            invoice.Reference,
            invoice.IsExport,
            invoice.ExportCountry,
            invoice.ExportDeclarationNo,
            invoice.ExportDeclarationDate,
            invoice.Status,
            invoice.ApprovedByUserId,
            invoice.ApprovedAt,
            invoice.CreatedAt,
            invoice.ReferrerType,
            invoice.ReferrerId,
            invoice.DiscountPct,
            invoice.GrandTotal,
            invoice.Terms,
            invoice.Lines.Select(x => new InvoiceLineDto(
                x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.VatAmount)).ToList(),
            glLines,
            invoice.CurrencyCode,
            invoice.ExchangeRate);
    }
}
