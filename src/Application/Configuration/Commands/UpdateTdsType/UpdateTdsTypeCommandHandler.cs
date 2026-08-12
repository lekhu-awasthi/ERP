using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdateTdsType;

public sealed class UpdateTdsTypeCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateTdsTypeCommand, UpdateTdsTypeResult>
{
    public async Task<UpdateTdsTypeResult> Handle(UpdateTdsTypeCommand request, CancellationToken cancellationToken)
    {
        var tdsType = await db.TdsTypes.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("TDS type not found.");

        var codeTaken = await db.TdsTypes.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Code == request.Code,
            cancellationToken);

        if (codeTaken)
        {
            throw new ConflictException($"A TDS type with code '{request.Code}' already exists.");
        }

        tdsType.Update(request.Code, request.Name, request.RatePct, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateTdsTypeResult(tdsType.Id, tdsType.Code, tdsType.Name, tdsType.RatePct, tdsType.IsActive);
    }
}
