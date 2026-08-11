using FluentValidation;

namespace ErpApp.Application.Accounting.Commands.CreateAccountGroup;

public sealed class CreateAccountGroupCommandValidator : AbstractValidator<CreateAccountGroupCommand>
{
    public CreateAccountGroupCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RootType).IsInEnum();
    }
}
