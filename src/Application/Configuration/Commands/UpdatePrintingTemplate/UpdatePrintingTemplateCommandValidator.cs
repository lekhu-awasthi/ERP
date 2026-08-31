using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdatePrintingTemplate;

public sealed class UpdatePrintingTemplateCommandValidator : AbstractValidator<UpdatePrintingTemplateCommand>
{
    public UpdatePrintingTemplateCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentType).IsInEnum();
    }
}
