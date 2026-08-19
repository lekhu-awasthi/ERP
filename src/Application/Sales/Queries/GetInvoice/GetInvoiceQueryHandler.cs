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
            var glEntry = await db.GlJournalEntries
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x => x.SourceDocumentType == DocumentType.Invoice && x.SourceDocumentId == invoice.Id, cancellationToken);

            glLines = glEntry?.Lines.Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
        }

        return new InvoiceDetailDto(
            invoice.Id,
            invoice.OrganizationId,
            invoice.ContactId,
            invoice.WarehouseId,
            invoice.Code,
            invoice.Date,
            invoice.Reference,
            invoice.Status,
            invoice.ApprovedByUserId,
            invoice.ApprovedAt,
            invoice.CreatedAt,
            invoice.ReferrerType,
            invoice.ReferrerId,
            invoice.DiscountPct,
            invoice.GrandTotal,
            invoice.Lines.Select(x => new InvoiceLineDto(
                x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.VatAmount)).ToList(),
            glLines);
    }
}
