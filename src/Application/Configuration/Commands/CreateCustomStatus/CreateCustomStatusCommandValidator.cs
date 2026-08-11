using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreateCustomStatus;

public sealed class CreateCustomStatusCommandValidator : AbstractValidator<CreateCustomStatusCommand>
{
    public CreateCustomStatusCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentType).IsInEnum();
    }
}
