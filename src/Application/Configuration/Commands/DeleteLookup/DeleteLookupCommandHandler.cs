using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
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

        // Phase 28: the one lookup row in this codebase that is not the tenant's to delete. Every
        // document defaults to the base currency and every exchange rate is quoted to it, so a
        // tenant that removed it could raise no document at all -- the same reasoning
        // Currency.Update uses to refuse deactivating it. Guarded here rather than by giving
        // Currency its own non-generic Delete command, because one type-test is a far smaller
        // change than opting Currency out of the generic pair it otherwise fits perfectly.
        if (entity is Currency { IsBaseCurrency: true } baseCurrency)
        {
            throw new ConflictException($"{baseCurrency.Code} is the base currency and cannot be removed.");
        }

        db.Set<TLookup>().Remove(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
