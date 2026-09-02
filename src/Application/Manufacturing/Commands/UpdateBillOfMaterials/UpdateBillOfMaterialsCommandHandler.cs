using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Commands.UpdateBillOfMaterials;

/// <summary>
/// Replaces all three child collections wholesale. The old children are removed through their own
/// DbSets rather than relying on collection-navigation fixup (phase-4 bug #1) and the new ones are
/// added through them too, because the parent here is already tracked and EF would otherwise mark
/// the appended children Modified rather than Added (phase-24 bug #1). Both halves of that pair
/// present as the same unhelpful DbUpdateConcurrencyException, so both are done explicitly.
/// </summary>
public sealed class UpdateBillOfMaterialsCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateBillOfMaterialsCommand, UpdateBillOfMaterialsResult>
{
    public async Task<UpdateBillOfMaterialsResult> Handle(
        UpdateBillOfMaterialsCommand request, CancellationToken cancellationToken)
    {
        var bom = await db.BillsOfMaterials
            .Include(x => x.RawMaterials).Include(x => x.ByProducts).Include(x => x.Expenses)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Bill of materials not found.");

        var productIds = ProductionRequestProducts.Collect(request.ProductId, request.RawMaterials, request.ByProducts);

        await ManufacturingValidation.EnsureProductsExistAsync(db, request.OrganizationId, productIds, cancellationToken);
        await ManufacturingValidation.EnsureProductsAreGoodsAsync(db, request.OrganizationId, productIds, cancellationToken);
        await ManufacturingValidation.EnsureCostTermsAreProductionCostsAsync(
            db, request.OrganizationId, request.Expenses.Select(x => x.CostTermId), cancellationToken);

        var clashes = await db.BillsOfMaterials.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.ProductId == request.ProductId && x.Id != request.Id,
            cancellationToken);

        if (clashes)
        {
            throw new ConflictException(
                "This product already has a bill of materials. Edit the existing one instead of adding a second.");
        }

        db.BomRawMaterialLines.RemoveRange(bom.RawMaterials.ToList());
        db.BomByProductLines.RemoveRange(bom.ByProducts.ToList());
        db.BomExpenseLines.RemoveRange(bom.Expenses.ToList());

        bom.UpdateHeader(
            request.ProductId, request.OutputQuantity, request.ManufactureOnEverySale, request.Notes, request.IsActive);
        bom.ClearLines();
        ProductionLineWriter.Fill(bom, request.RawMaterials, request.ByProducts, request.Expenses);
        bom.EnsureByProductAllocationIsSane();

        db.BomRawMaterialLines.AddRange(bom.RawMaterials.ToList());
        db.BomByProductLines.AddRange(bom.ByProducts.ToList());
        db.BomExpenseLines.AddRange(bom.Expenses.ToList());

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateBillOfMaterialsResult(bom.Id);
    }
}
