using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.UpdateSecondaryUnit;

public sealed class UpdateSecondaryUnitCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateSecondaryUnitCommand, SecondaryUnitResult>
{
    public async Task<SecondaryUnitResult> Handle(
        UpdateSecondaryUnitCommand request, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .Include(x => x.SecondaryUnits)
            .SingleOrDefaultAsync(
                x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        ProductVariantRules.EnsureCarriesAUnitMatrix(product.Name, product.HasVariants);

        if (product.SecondaryUnits.All(x => x.Id != request.SecondaryUnitId))
        {
            throw new NotFoundException("Secondary unit not found.");
        }

        // Loaded through Include, so this row is tracked and the mutation is picked up -- unlike the
        // add, which appends to an untracked collection and must go through the child DbSet.
        var row = product.UpdateSecondaryUnit(
            request.SecondaryUnitId, request.ConversionRate, request.SellingPrice, request.PurchasePrice);

        await db.SaveChangesAsync(cancellationToken);

        return new SecondaryUnitResult(
            row.Id, row.ProductId, row.UnitId, row.ConversionRate, row.SellingPrice, row.PurchasePrice);
    }
}
