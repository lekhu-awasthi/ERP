using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.DeleteLookup;

public sealed class DeleteLookupCommandHandler<TLookup>(IAppDbContext db)
    : IRequestHandler<DeleteLookupCommand<TLookup>, Unit>
    where TLookup : class, ITenantLookupEntity
{
    public async Task<Unit> Handle(DeleteLookupCommand<TLookup> request, CancellationToken cancellationToken)
    {
        var entity = await db.Set<TLookup>().SingleOrDefaultAsync(
            x => EF.Property<Guid>(x, "Id") == request.Id
                 && EF.Property<Guid>(x, nameof(ITenantLookupEntity.OrganizationId)) == request.OrganizationId,
            cancellationToken)
            ?? throw new NotFoundException($"{typeof(TLookup).Name} not found.");

        db.Set<TLookup>().Remove(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
