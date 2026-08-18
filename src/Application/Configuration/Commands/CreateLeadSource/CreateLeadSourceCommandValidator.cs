using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreateLeadSource;

public sealed class CreateLeadSourceCommandValidator : AbstractValidator<CreateLeadSourceCommand>
{
    public CreateLeadSourceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
