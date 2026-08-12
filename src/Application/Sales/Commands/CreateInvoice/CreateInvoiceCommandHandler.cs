using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using MediatR;

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

        var invoice = Invoice.Create(
            request.OrganizationId, request.ContactId, request.WarehouseId, request.Date, request.Reference,
            request.ReferrerType, request.ReferrerId);
        foreach (var line in request.Lines)
        {
            invoice.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate);
        }

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateInvoiceResult(invoice.Id, invoice.Code, invoice.Status);
    }
}
