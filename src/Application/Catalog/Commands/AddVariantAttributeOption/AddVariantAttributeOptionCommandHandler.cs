using ErpApp.Application.Catalog.Commands.CreateVariantAttribute;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.AddVariantAttributeOption;

public sealed class AddVariantAttributeOptionCommandHandler(IAppDbContext db)
    : IRequestHandler<AddVariantAttributeOptionCommand, VariantAttributeResult>
{
    public async Task<VariantAttributeResult> Handle(AddVariantAttributeOptionCommand request, CancellationToken cancellationToken)
    {
        var attribute = await db.VariantAttributes
            .Include(x => x.Options)
            .SingleOrDefaultAsync(x => x.Id == request.AttributeId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Variant attribute not found.");

        try
        {
            attribute.AddOption(request.Value);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return VariantAttributeMapper.ToResult(attribute);
    }
}
