using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Commands.ApproveProductionOrder;

public sealed class ApproveProductionOrderCommandHandler(
    IAppDbContext db, IDocumentNumberGenerator numberGenerator, ICurrentUserService currentUser)
    : IRequestHandler<ApproveProductionOrderCommand, ApproveProductionOrderResult>
{
    public async Task<ApproveProductionOrderResult> Handle(
        ApproveProductionOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await db.ProductionOrders
            .Include(x => x.RawMaterials)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Production order not found.");

        if (order.Status != ProductionOrderStatus.Draft)
        {
            throw new ConflictException("Only a Draft production order can be approved.");
        }

        if (order.RawMaterials.Count == 0)
        {
            throw new ConflictException("A production order needs at least one raw material to be approved.");
        }

        var code = await numberGenerator.GetNextNumberAsync(
            request.OrganizationId, DocumentType.ProductionOrder, cancellationToken);

        order.Approve(currentUser.UserId, code);
        await db.SaveChangesAsync(cancellationToken);

        return new ApproveProductionOrderResult(order.Id, order.Code, order.Status, order.ApprovedAt);
    }
}
