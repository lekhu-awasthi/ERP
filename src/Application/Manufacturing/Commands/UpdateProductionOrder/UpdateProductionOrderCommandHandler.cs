using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Manufacturing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Commands.UpdateProductionOrder;

/// <summary>Full-collection replace through the children's own DbSets -- phase-4 bug #1 and
/// phase-24 bug #1, which present identically and are both avoided the same way.</summary>
public sealed class UpdateProductionOrderCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateProductionOrderCommand, UpdateProductionOrderResult>
{
    public async Task<UpdateProductionOrderResult> Handle(
        UpdateProductionOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await db.ProductionOrders
            .Include(x => x.RawMaterials).Include(x => x.ByProducts).Include(x => x.Expenses)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Production order not found.");

        if (order.Status != ProductionOrderStatus.Draft)
        {
            throw new ConflictException("Only a Draft production order can be edited.");
        }

        var productIds = ProductionRequestProducts.Collect(request.ProductId, request.RawMaterials, request.ByProducts);

        await ManufacturingValidation.EnsureProductsExistAsync(db, request.OrganizationId, productIds, cancellationToken);
        await ManufacturingValidation.EnsureProductsAreGoodsAsync(db, request.OrganizationId, productIds, cancellationToken);
        await ManufacturingValidation.EnsureCostTermsAreProductionCostsAsync(
            db, request.OrganizationId, request.Expenses.Select(x => x.CostTermId), cancellationToken);
        await ManufacturingValidation.EnsureBillOfMaterialsExistsAsync(
            db, request.OrganizationId, request.BillOfMaterialsId, cancellationToken);

        db.ProductionOrderRawMaterialLines.RemoveRange(order.RawMaterials.ToList());
        db.ProductionOrderByProductLines.RemoveRange(order.ByProducts.ToList());
        db.ProductionOrderExpenseLines.RemoveRange(order.Expenses.ToList());

        order.UpdateHeader(
            request.Date, request.Reference, request.ProductId, request.OutputQuantity,
            request.BillOfMaterialsId, request.Notes);
        order.ClearLines();
        ProductionLineWriter.Fill(order, request.RawMaterials, request.ByProducts, request.Expenses);
        order.EnsureByProductAllocationIsSane();

        db.ProductionOrderRawMaterialLines.AddRange(order.RawMaterials.ToList());
        db.ProductionOrderByProductLines.AddRange(order.ByProducts.ToList());
        db.ProductionOrderExpenseLines.AddRange(order.Expenses.ToList());

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateProductionOrderResult(order.Id, order.Code, order.Status);
    }
}
