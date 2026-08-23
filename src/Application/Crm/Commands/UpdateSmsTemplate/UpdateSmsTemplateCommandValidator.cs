using FluentValidation;

namespace ErpApp.Application.Crm.Commands.UpdateSmsTemplate;

public sealed class UpdateSmsTemplateCommandValidator : AbstractValidator<UpdateSmsTemplateCommand>
{
    public UpdateSmsTemplateCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(500);
    }
}
