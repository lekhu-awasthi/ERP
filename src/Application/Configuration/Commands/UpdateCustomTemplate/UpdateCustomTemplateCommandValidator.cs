using ErpApp.Application.Common.Validation;
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
        // Phase 39: the 4,000 cap went with the plain textarea -- see CustomTemplateConfiguration.
        RuleFor(x => x.Body).NotEmpty().RichText("Template body");
    }
}
