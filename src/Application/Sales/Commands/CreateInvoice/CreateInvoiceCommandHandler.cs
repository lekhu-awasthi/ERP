using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.CreateInvoice;

public sealed class CreateInvoiceCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateInvoiceCommand, CreateInvoiceResult>
{
    public async Task<CreateInvoiceResult> Handle(CreateInvoiceCommand request, CancellationToken cancellationToken)
    {
        await SalesValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, ContactType.Customer, cancellationToken);
        await SalesValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.WarehouseId, cancellationToken);
        await SalesValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        if (request.ReferrerType == DocumentType.Quotation && request.ReferrerId is { } quotationId)
        {
            var quotation = await db.Quotations.SingleOrDefaultAsync(
                x => x.Id == quotationId && x.OrganizationId == request.OrganizationId, cancellationToken)
                ?? throw new NotFoundException("Quotation not found.");

            if (quotation.Status != QuotationStatus.Approved)
            {
                throw new ConflictException("This quotation has already been converted to an Invoice, or is not Approved.");
            }

            quotation.MarkConverted();
        }

        var invoice = Invoice.Create(
            request.OrganizationId, request.ContactId, request.WarehouseId, request.Date, request.Reference,
            request.ReferrerType, request.ReferrerId, request.DiscountPct,
            request.IsExport, request.ExportCountry, request.ExportDeclarationNo, request.ExportDeclarationDate);
        invoice.SetTerms(request.Terms);

        foreach (var line in request.Lines)
        {
            invoice.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct);
        }

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateInvoiceResult(invoice.Id, invoice.Code, invoice.Status);
    }
}
