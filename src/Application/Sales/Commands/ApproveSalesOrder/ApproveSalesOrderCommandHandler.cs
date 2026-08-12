using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.ApproveSalesOrder;

public sealed class ApproveSalesOrderCommandHandler(
    IAppDbContext db, IDocumentNumberGenerator numberGenerator, ICurrentUserService currentUser)
    : IRequestHandler<ApproveSalesOrderCommand, ApproveSalesOrderResult>
{
    public async Task<ApproveSalesOrderResult> Handle(ApproveSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var salesOrder = await db.SalesOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Sales order not found.");

        if (salesOrder.Status != SalesOrderStatus.Draft)
        {
            throw new ConflictException("Only a Draft sales order can be approved.");
        }

        if (salesOrder.Lines.Count == 0)
        {
            throw new ConflictException("A sales order needs at least one line to be approved.");
        }

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.SalesOrder, cancellationToken);

        salesOrder.Approve(currentUser.UserId, code);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveSalesOrderResult(salesOrder.Id, salesOrder.Code, salesOrder.Status, salesOrder.ApprovedAt);
    }
}
