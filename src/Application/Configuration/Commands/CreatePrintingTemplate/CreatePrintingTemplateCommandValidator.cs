using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreatePrintingTemplate;

public sealed class CreatePrintingTemplateCommandValidator : AbstractValidator<CreatePrintingTemplateCommand>
{
    public CreatePrintingTemplateCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentType).IsInEnum();
    }
}
