using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Communications.Commands.SetDefaultEmailTemplate;

/// <summary>Backs the live template card's "Set as Default" kebab action. Exactly one default per
/// context, so the previous one is cleared in the same save.</summary>
public sealed record SetDefaultEmailTemplateCommand(Guid OrganizationId, Guid Id)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.EmailTemplateManage;
}

public sealed class SetDefaultEmailTemplateCommandHandler(IAppDbContext db)
    : IRequestHandler<SetDefaultEmailTemplateCommand, Unit>
{
    public async Task<Unit> Handle(SetDefaultEmailTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.EmailTemplates.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Email template not found.");

        if (!template.IsActive)
        {
            throw new ConflictException("An inactive template cannot be the default for its context.");
        }

        var siblings = await db.EmailTemplates
            .Where(x => x.OrganizationId == request.OrganizationId
                        && x.Context == template.Context
                        && x.Id != template.Id
                        && x.IsDefault)
            .ToListAsync(cancellationToken);

        foreach (var sibling in siblings)
        {
            sibling.ClearDefault();
        }

        template.MarkAsDefault();
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
