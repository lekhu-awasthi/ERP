using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.BankBalanceHistory;

public sealed class BankBalanceHistoryQueryValidator : AbstractValidator<BankBalanceHistoryQuery>
{
    public BankBalanceHistoryQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.BankAccountId).NotEmpty();

        RuleFor(x => x.Days)
            .InclusiveBetween(1, BankBalanceHistoryQuery.MaxDays)
            .WithMessage($"The balance history window must be between 1 and {BankBalanceHistoryQuery.MaxDays} days.");
    }
}
