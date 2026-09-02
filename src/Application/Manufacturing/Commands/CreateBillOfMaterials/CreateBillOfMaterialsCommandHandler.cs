using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Manufacturing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Commands.CreateBillOfMaterials;

public sealed class CreateBillOfMaterialsCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateBillOfMaterialsCommand, CreateBillOfMaterialsResult>
{
    public async Task<CreateBillOfMaterialsResult> Handle(
        CreateBillOfMaterialsCommand request, CancellationToken cancellationToken)
    {
        var productIds = ProductionRequestProducts.Collect(request.ProductId, request.RawMaterials, request.ByProducts);

        await ManufacturingValidation.EnsureProductsExistAsync(db, request.OrganizationId, productIds, cancellationToken);
        await ManufacturingValidation.EnsureProductsAreGoodsAsync(db, request.OrganizationId, productIds, cancellationToken);
        await ManufacturingValidation.EnsureCostTermsAreProductionCostsAsync(
            db, request.OrganizationId, request.Expenses.Select(x => x.CostTermId), cancellationToken);

        var alreadyExists = await db.BillsOfMaterials.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.ProductId == request.ProductId, cancellationToken);

        if (alreadyExists)
        {
            throw new ConflictException(
                "This product already has a bill of materials. Edit the existing one instead of adding a second.");
        }

        var bom = BillOfMaterials.Create(
            request.OrganizationId, request.ProductId, request.OutputQuantity, request.ManufactureOnEverySale, request.Notes);

        ProductionLineWriter.Fill(bom, request.RawMaterials, request.ByProducts, request.Expenses);
        bom.EnsureByProductAllocationIsSane();

        db.BillsOfMaterials.Add(bom);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateBillOfMaterialsResult(bom.Id);
    }
}
