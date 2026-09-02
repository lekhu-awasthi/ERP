using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.CreateVariantAttribute;

public sealed class CreateVariantAttributeCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateVariantAttributeCommand, VariantAttributeResult>
{
    public async Task<VariantAttributeResult> Handle(CreateVariantAttributeCommand request, CancellationToken cancellationToken)
    {
        var attribute = VariantAttribute.Create(request.OrganizationId, request.Name);

        foreach (var option in request.Options)
        {
            // AddOption's own duplicate guard throws InvalidOperationException (a 500); a duplicate
            // typed into the create form is user error, so it is surfaced as a 409 instead.
            try
            {
                attribute.AddOption(option);
            }
            catch (InvalidOperationException ex)
            {
                throw new ConflictException(ex.Message);
            }
        }

        db.VariantAttributes.Add(attribute);
        await db.SaveChangesAsync(cancellationToken);

        return VariantAttributeMapper.ToResult(attribute);
    }
}
