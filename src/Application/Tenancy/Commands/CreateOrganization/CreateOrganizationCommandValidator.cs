using FluentValidation;

namespace ErpApp.Application.Tenancy.Commands.CreateOrganization;

public sealed class CreateOrganizationCommandValidator : AbstractValidator<CreateOrganizationCommand>
{
    public CreateOrganizationCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Industry).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.AccountingStartDate).NotEqual(default(DateOnly));

        RuleFor(x => x.WorkspaceName)
            .NotEmpty()
            .MaximumLength(63)
            .Matches("^[a-zA-Z0-9][a-zA-Z0-9-]*$")
            .WithMessage("Workspace name can only contain letters, numbers, and hyphens.");

        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.PanNumber).MaximumLength(50);
        RuleFor(x => x.Website).MaximumLength(256);

        // Phase 27b -- matches RegisterUserCommandValidator's rule. The handler re-checks for blank
        // as well: this turns a missing token into a 400 with a field name, and the handler's own
        // guard is what makes the check impossible to bypass by any other caller.
        RuleFor(x => x.TurnstileToken).NotEmpty();
    }
}
