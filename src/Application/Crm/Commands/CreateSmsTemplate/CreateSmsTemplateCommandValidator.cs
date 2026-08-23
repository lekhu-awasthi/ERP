using FluentValidation;

namespace ErpApp.Application.Crm.Commands.CreateSmsTemplate;

public sealed class CreateSmsTemplateCommandValidator : AbstractValidator<CreateSmsTemplateCommand>
{
    public CreateSmsTemplateCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(500);
    }
}
