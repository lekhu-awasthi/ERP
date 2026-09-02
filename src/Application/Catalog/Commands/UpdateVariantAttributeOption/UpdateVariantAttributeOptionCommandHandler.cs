using ErpApp.Application.Catalog.Commands.CreateVariantAttribute;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.UpdateVariantAttributeOption;

public sealed class UpdateVariantAttributeOptionCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateVariantAttributeOptionCommand, VariantAttributeResult>
{
    public async Task<VariantAttributeResult> Handle(
        UpdateVariantAttributeOptionCommand request, CancellationToken cancellationToken)
    {
        var attribute = await db.VariantAttributes
            .Include(x => x.Options)
            .SingleOrDefaultAsync(x => x.Id == request.AttributeId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Variant attribute not found.");

        try
        {
            attribute.RenameOption(request.OptionId, request.Value);

            if (request.IsActive)
            {
                attribute.ReactivateOption(request.OptionId);
            }
            else
            {
                attribute.DeactivateOption(request.OptionId);
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return VariantAttributeMapper.ToResult(attribute);
    }
}
