using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.UpdateUnitOfMeasurement;

public sealed class UpdateUnitOfMeasurementCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateUnitOfMeasurementCommand, UpdateUnitOfMeasurementResult>
{
    public async Task<UpdateUnitOfMeasurementResult> Handle(UpdateUnitOfMeasurementCommand request, CancellationToken cancellationToken)
    {
        var unit = await db.UnitsOfMeasurement.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Unit of measurement not found.");

        var nameTaken = await db.UnitsOfMeasurement.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"A unit of measurement named '{request.Name}' already exists.");
        }

        unit.Update(request.Name, request.ShortName, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateUnitOfMeasurementResult(unit.Id, unit.Name, unit.ShortName, unit.IsActive);
    }
}
