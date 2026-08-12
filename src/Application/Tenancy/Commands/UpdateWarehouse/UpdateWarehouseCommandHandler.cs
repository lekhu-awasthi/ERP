using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.UpdateWarehouse;

public sealed class UpdateWarehouseCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateWarehouseCommand, UpdateWarehouseResult>
{
    public async Task<UpdateWarehouseResult> Handle(UpdateWarehouseCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Warehouse not found.");

        var nameTaken = await db.Warehouses.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"A warehouse named '{request.Name}' already exists.");
        }

        warehouse.Update(request.Name, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateWarehouseResult(warehouse.Id, warehouse.Name, warehouse.IsActive);
    }
}
