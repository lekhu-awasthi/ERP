using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.BankReconciliationReport;

public sealed class BankReconciliationReportQueryValidator
    : AbstractValidator<BankReconciliationReportQuery>
{
    public BankReconciliationReportQueryValidator()
    {
        RuleFor(x => x.BankAccountId).NotEmpty();
        RuleFor(x => x.UnrecognizedPage).GreaterThan(0);
        RuleFor(x => x.UnrecognizedPageSize).InclusiveBetween(1, 200);
    }
}
