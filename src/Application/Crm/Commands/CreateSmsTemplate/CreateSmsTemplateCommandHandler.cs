using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Crm;
using MediatR;

namespace ErpApp.Application.Crm.Commands.CreateSmsTemplate;

public sealed class CreateSmsTemplateCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateSmsTemplateCommand, SmsTemplateResult>
{
    public async Task<SmsTemplateResult> Handle(CreateSmsTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = SmsTemplate.Create(request.OrganizationId, request.Title, request.Content);

        db.SmsTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        return new SmsTemplateResult(template.Id, template.Title, template.Content);
    }
}
