using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.SetPrintingTemplateLocationChoice;

public sealed class SetPrintingTemplateLocationChoiceCommandValidator : AbstractValidator<SetPrintingTemplateLocationChoiceCommand>
{
    public SetPrintingTemplateLocationChoiceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.PrintingTemplateId).NotEmpty();
    }
}
