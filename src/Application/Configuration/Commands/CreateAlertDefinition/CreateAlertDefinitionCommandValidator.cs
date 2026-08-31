using ErpApp.Domain.Configuration;
using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreateAlertDefinition;

public sealed class CreateAlertDefinitionCommandValidator : AbstractValidator<CreateAlertDefinitionCommand>
{
    public CreateAlertDefinitionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Medium).IsInEnum();
        RuleFor(x => x.AlertType).IsInEnum();
        RuleFor(x => x.Frequency).IsInEnum();
        RuleFor(x => x.Recipients)
            .NotEmpty()
            .MaximumLength(AlertDefinitionValidation.MaxRecipientsLength)
            .Must(AlertDefinitionValidation.HasAtLeastOneRecipient)
            .WithMessage("At least one recipient email address is required.")
            .Must(AlertDefinitionValidation.AllRecipientsAreValidEmails)
            .WithMessage("Every recipient must be a valid email address, separated by commas.");
    }
}
