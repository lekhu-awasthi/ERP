using FluentValidation;

namespace ErpApp.Application.Accounting.Commands.CreateAccount;

public sealed class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand>
{
    public CreateAccountCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.AccountNumber).MaximumLength(50);
    }
}
