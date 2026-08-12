using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.UpdateSalesOrder;

public sealed class UpdateSalesOrderCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateSalesOrderCommand, UpdateSalesOrderResult>
{
    public async Task<UpdateSalesOrderResult> Handle(UpdateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var salesOrder = await db.SalesOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Sales order not found.");

        if (salesOrder.Status != SalesOrderStatus.Draft)
        {
            throw new ConflictException("Only a Draft sales order can be edited.");
        }

        await SalesValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, ContactType.Customer, cancellationToken);
        await SalesValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var oldLines = salesOrder.Lines.ToList();

        salesOrder.UpdateHeader(request.ContactId, request.Date, request.DeliveryDate, request.Reference);
        salesOrder.ClearLines();
        foreach (var line in request.Lines)
        {
            salesOrder.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate);
        }

        db.SalesOrderLines.RemoveRange(oldLines);
        db.SalesOrderLines.AddRange(salesOrder.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateSalesOrderResult(salesOrder.Id, salesOrder.Code, salesOrder.Status);
    }
}
