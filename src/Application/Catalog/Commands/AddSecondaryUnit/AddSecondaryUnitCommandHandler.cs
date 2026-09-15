using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.AddSecondaryUnit;

public sealed class AddSecondaryUnitCommandHandler(IAppDbContext db)
    : IRequestHandler<AddSecondaryUnitCommand, AddSecondaryUnitResult>
{
    public async Task<AddSecondaryUnitResult> Handle(AddSecondaryUnitCommand request, CancellationToken cancellationToken)
    {
        // Include: the duplicate-unit and primary-unit refusals below read this collection, and an
        // un-Included navigation is empty rather than absent -- it would let every duplicate through.
        var product = await db.Products
            .Include(x => x.SecondaryUnits)
            .SingleOrDefaultAsync(
                x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        // Phase 45 -- a variant parent has no unit matrix. Phase 24's sweep-guard allow-list excused
        // this handler on the grounds that "attaching a secondary unit to a parent moves nothing and
        // reconciles against nothing", which is the argument for refusing it; the live read agreed,
        // so the exemption is gone and this goes through the rule like every other handler.
        ProductVariantRules.EnsureCarriesAUnitMatrix(product.Name, product.HasVariants);

        if (request.UnitId == product.PrimaryUnitId)
        {
            throw new ConflictException(
                "That is this product's primary unit, which it already sells in; a secondary unit is a different one.");
        }

        if (product.SecondaryUnits.Any(x => x.UnitId == request.UnitId))
        {
            throw new ConflictException(
                "This product already has a secondary unit for that unit of measurement. Edit that row instead.");
        }

        var unitExists = await db.UnitsOfMeasurement.AnyAsync(
            x => x.Id == request.UnitId && x.OrganizationId == request.OrganizationId, cancellationToken);

        if (!unitExists)
        {
            throw new NotFoundException("Unit of measurement not found.");
        }

        // product.AddSecondaryUnit only appends to the aggregate's in-memory (untracked)
        // collection -- EF's change tracker doesn't observe that mutation since the navigation
        // wasn't loaded via Include, so the new child is added to its own DbSet explicitly (its
        // FK is already set by the factory method).
        var secondaryUnit = product.AddSecondaryUnit(
            request.UnitId, request.ConversionRate, request.SellingPrice, request.PurchasePrice);
        db.ProductSecondaryUnits.Add(secondaryUnit);
        await db.SaveChangesAsync(cancellationToken);

        return new AddSecondaryUnitResult(secondaryUnit.Id, secondaryUnit.ProductId, secondaryUnit.UnitId, secondaryUnit.ConversionRate);
    }
}
