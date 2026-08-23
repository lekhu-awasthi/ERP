using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Crm.Commands.DeleteSmsTemplate;

public sealed class DeleteSmsTemplateCommandHandler(IAppDbContext db) : IRequestHandler<DeleteSmsTemplateCommand, Unit>
{
    public async Task<Unit> Handle(DeleteSmsTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.SmsTemplates.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("SMS template not found.");

        db.SmsTemplates.Remove(template);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
