using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;

public sealed class CreateUnitOfMeasurementCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateUnitOfMeasurementCommand, CreateUnitOfMeasurementResult>
{
    public async Task<CreateUnitOfMeasurementResult> Handle(CreateUnitOfMeasurementCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.UnitsOfMeasurement.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A unit of measurement named '{request.Name}' already exists.");
        }

        var unit = UnitOfMeasurement.Create(request.OrganizationId, request.Name, request.ShortName);
        db.UnitsOfMeasurement.Add(unit);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateUnitOfMeasurementResult(unit.Id, unit.Name, unit.ShortName);
    }
}
