using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Manufacturing;
using MediatR;

namespace ErpApp.Application.Manufacturing.Commands.CreateProductionOrder;

public sealed class CreateProductionOrderCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateProductionOrderCommand, CreateProductionOrderResult>
{
    public async Task<CreateProductionOrderResult> Handle(
        CreateProductionOrderCommand request, CancellationToken cancellationToken)
    {
        var productIds = ProductionRequestProducts.Collect(request.ProductId, request.RawMaterials, request.ByProducts);

        await ManufacturingValidation.EnsureProductsExistAsync(db, request.OrganizationId, productIds, cancellationToken);
        await ManufacturingValidation.EnsureProductsAreGoodsAsync(db, request.OrganizationId, productIds, cancellationToken);
        await ManufacturingValidation.EnsureCostTermsAreProductionCostsAsync(
            db, request.OrganizationId, request.Expenses.Select(x => x.CostTermId), cancellationToken);
        await ManufacturingValidation.EnsureBillOfMaterialsExistsAsync(
            db, request.OrganizationId, request.BillOfMaterialsId, cancellationToken);

        var order = ProductionOrder.Create(
            request.OrganizationId, request.Date, request.Reference, request.ProductId, request.OutputQuantity,
            request.BillOfMaterialsId, request.Notes);

        ProductionLineWriter.Fill(order, request.RawMaterials, request.ByProducts, request.Expenses);
        order.EnsureByProductAllocationIsSane();

        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateProductionOrderResult(order.Id, order.Code, order.Status);
    }
}
