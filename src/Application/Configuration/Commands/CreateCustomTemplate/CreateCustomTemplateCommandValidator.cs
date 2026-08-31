using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreateCustomTemplate;

public sealed class CreateCustomTemplateCommandValidator : AbstractValidator<CreateCustomTemplateCommand>
{
    public CreateCustomTemplateCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
    }
}
