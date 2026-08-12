using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateSalesOrder;

public sealed class CreateSalesOrderCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateSalesOrderCommand, CreateSalesOrderResult>
{
    public async Task<CreateSalesOrderResult> Handle(CreateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        await SalesValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, ContactType.Customer, cancellationToken);
        await SalesValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var salesOrder = SalesOrder.Create(request.OrganizationId, request.ContactId, request.Date, request.DeliveryDate, request.Reference);
        foreach (var line in request.Lines)
        {
            salesOrder.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate);
        }

        db.SalesOrders.Add(salesOrder);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateSalesOrderResult(salesOrder.Id, salesOrder.Code, salesOrder.Status);
    }
}
