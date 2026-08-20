using FluentValidation;

namespace ErpApp.Application.Accounting.Commands.CreateOrUpdateOpeningBalanceLine;

public sealed class CreateOrUpdateOpeningBalanceLineCommandValidator : AbstractValidator<CreateOrUpdateOpeningBalanceLineCommand>
{
    public CreateOrUpdateOpeningBalanceLineCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Debit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Credit).GreaterThanOrEqualTo(0);
        RuleFor(x => x)
            .Must(x => (x.Debit > 0) != (x.Credit > 0))
            .WithMessage("Exactly one of Debit/Credit must be greater than zero.");
    }
}
