using ErpApp.Application.Catalog.Commands.CreateVariantAttribute;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.UpdateVariantAttribute;

public sealed class UpdateVariantAttributeCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateVariantAttributeCommand, VariantAttributeResult>
{
    public async Task<VariantAttributeResult> Handle(UpdateVariantAttributeCommand request, CancellationToken cancellationToken)
    {
        var attribute = await db.VariantAttributes
            .Include(x => x.Options)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Variant attribute not found.");

        attribute.Update(request.Name, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return VariantAttributeMapper.ToResult(attribute);
    }
}
