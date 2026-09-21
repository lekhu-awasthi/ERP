using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.GetBankReconciliation;

public sealed class GetBankReconciliationQueryValidator : AbstractValidator<GetBankReconciliationQuery>
{
    public GetBankReconciliationQueryValidator()
    {
        RuleFor(x => x.BankAccountId).NotEmpty();
        RuleFor(x => x.ReconciliationId).NotEmpty();
    }
}
