using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.SetAlertDefinitionActive;

public sealed class SetAlertDefinitionActiveCommandValidator : AbstractValidator<SetAlertDefinitionActiveCommand>
{
    public SetAlertDefinitionActiveCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
    }
}
