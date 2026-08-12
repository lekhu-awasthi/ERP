using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.CreateWarehouse;

public sealed class CreateWarehouseCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateWarehouseCommand, CreateWarehouseResult>
{
    public async Task<CreateWarehouseResult> Handle(CreateWarehouseCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.Warehouses.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A warehouse named '{request.Name}' already exists.");
        }

        var warehouse = Warehouse.Create(request.OrganizationId, request.Name);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateWarehouseResult(warehouse.Id, warehouse.Name);
    }
}
