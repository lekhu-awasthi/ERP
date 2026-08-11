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
        var product = await db.Products.SingleOrDefaultAsync(
            x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");

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
