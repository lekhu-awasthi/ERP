using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdateCustomTemplate;

public sealed class UpdateCustomTemplateCommandValidator : AbstractValidator<UpdateCustomTemplateCommand>
{
    public UpdateCustomTemplateCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
    }
}
