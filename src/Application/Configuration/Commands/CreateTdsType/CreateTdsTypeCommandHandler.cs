using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreateTdsType;

public sealed class CreateTdsTypeCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateTdsTypeCommand, CreateTdsTypeResult>
{
    public async Task<CreateTdsTypeResult> Handle(CreateTdsTypeCommand request, CancellationToken cancellationToken)
    {
        var codeExists = await db.TdsTypes.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Code == request.Code, cancellationToken);

        if (codeExists)
        {
            throw new ConflictException($"A TDS type with code '{request.Code}' already exists.");
        }

        var tdsType = TdsType.Create(request.OrganizationId, request.Code, request.Name, request.RatePct);
        db.TdsTypes.Add(tdsType);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateTdsTypeResult(tdsType.Id, tdsType.Code, tdsType.Name, tdsType.RatePct);
    }
}
