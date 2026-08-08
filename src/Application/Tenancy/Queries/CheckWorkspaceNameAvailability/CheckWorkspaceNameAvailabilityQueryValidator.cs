using FluentValidation;

namespace ErpApp.Application.Tenancy.Queries.CheckWorkspaceNameAvailability;

public sealed class CheckWorkspaceNameAvailabilityQueryValidator : AbstractValidator<CheckWorkspaceNameAvailabilityQuery>
{
    public CheckWorkspaceNameAvailabilityQueryValidator()
    {
        RuleFor(x => x.WorkspaceName)
            .NotEmpty()
            .MaximumLength(63)
            .Matches("^[a-zA-Z0-9][a-zA-Z0-9-]*$")
            .WithMessage("Workspace name can only contain letters, numbers, and hyphens.");
    }
}
