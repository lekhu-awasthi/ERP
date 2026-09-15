using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.DeleteSecondaryUnit;

public sealed class DeleteSecondaryUnitCommandHandler(IAppDbContext db)
    : IRequestHandler<DeleteSecondaryUnitCommand, Unit>
{
    public async Task<Unit> Handle(DeleteSecondaryUnitCommand request, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .Include(x => x.SecondaryUnits)
            .SingleOrDefaultAsync(
                x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        // Deliberately NOT guarded by ProductVariantRules.EnsureCarriesAUnitMatrix, unlike the add
        // and the update. A product that already held secondary units can be promoted to a variant
        // parent afterwards -- SetProductVariantAttributesCommand does not refuse that, and should
        // not, because the pool is the thing being set. Its rows are then stale, and refusing the
        // delete would make them permanent: invisible on the form and unremovable through the API.
        // **The delete is the repair, so it is the one verb a parent keeps.** Found by driving the
        // browser pass rather than by any test, because every test constructed the product in its
        // final role.
        if (product.SecondaryUnits.All(x => x.Id != request.SecondaryUnitId))
        {
            throw new NotFoundException("Secondary unit not found.");
        }

        // Removed through the child DbSet rather than left to collection-navigation fixup, the same
        // reason SetLocations and SetVariantAttributeUsages report their changes (phase-4 bug #1,
        // phase-24 bug #1).
        var removed = product.RemoveSecondaryUnit(request.SecondaryUnitId);
        db.ProductSecondaryUnits.Remove(removed);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
