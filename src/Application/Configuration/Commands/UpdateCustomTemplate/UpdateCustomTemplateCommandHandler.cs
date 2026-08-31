using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdateCustomTemplate;

public sealed class UpdateCustomTemplateCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateCustomTemplateCommand, UpdateCustomTemplateResult>
{
    public async Task<UpdateCustomTemplateResult> Handle(UpdateCustomTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.CustomTemplates.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Custom template not found.");

        var nameTaken = await db.CustomTemplates.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.Id != request.Id
                 && x.Type == request.Type
                 && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"A custom template named '{request.Name}' already exists for {request.Type}.");
        }

        template.Update(request.Name, request.Type, request.Body, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateCustomTemplateResult(
            template.Id, template.Name, template.Type, template.Body, template.IsDefault, template.IsActive);
    }
}
