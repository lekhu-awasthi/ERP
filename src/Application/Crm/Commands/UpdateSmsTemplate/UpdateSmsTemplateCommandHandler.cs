using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Crm.Commands.CreateSmsTemplate;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Crm.Commands.UpdateSmsTemplate;

public sealed class UpdateSmsTemplateCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateSmsTemplateCommand, SmsTemplateResult>
{
    public async Task<SmsTemplateResult> Handle(UpdateSmsTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.SmsTemplates.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("SMS template not found.");

        template.Update(request.Title, request.Content);
        await db.SaveChangesAsync(cancellationToken);

        return new SmsTemplateResult(template.Id, template.Title, template.Content);
    }
}
