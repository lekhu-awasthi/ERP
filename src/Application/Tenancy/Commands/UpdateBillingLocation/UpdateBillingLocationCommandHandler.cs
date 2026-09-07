using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.UpdateBillingLocation;

public sealed class UpdateBillingLocationCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateBillingLocationCommand>
{
    public async Task Handle(UpdateBillingLocationCommand request, CancellationToken cancellationToken)
    {
        var location = await db.BillingLocations.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Billing location not found.");

        var code = request.Code.Trim();

        var codeTaken = await db.BillingLocations.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Code == code && x.Id != request.Id,
            cancellationToken);

        if (codeTaken)
        {
            throw new ConflictException($"A billing location with code '{code}' already exists.");
        }

        if (request.WarehouseId is { } warehouseId)
        {
            var warehouseExists = await db.Warehouses.AnyAsync(
                x => x.Id == warehouseId && x.OrganizationId == request.OrganizationId, cancellationToken);

            if (!warehouseExists)
            {
                throw new NotFoundException("Warehouse not found.");
            }
        }

        try
        {
            location.Update(code, request.Name, request.Address, request.WarehouseId, request.IsActive);
        }
        catch (InvalidOperationException ex)
        {
            // Deactivating the HeadOffice location. A 409 rather than the Domain exception's 500,
            // the same mapping UpdateCurrencyCommandHandler uses for the base-currency guard.
            throw new ConflictException(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
