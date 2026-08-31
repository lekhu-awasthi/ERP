using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdateAlertDefinition;

public sealed class UpdateAlertDefinitionCommandValidator : AbstractValidator<UpdateAlertDefinitionCommand>
{
    public UpdateAlertDefinitionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
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
