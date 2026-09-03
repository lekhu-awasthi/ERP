using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.GeneralLedgerMaster;

public sealed class GeneralLedgerMasterQueryValidator : AbstractValidator<GeneralLedgerMasterQuery>
{
    public GeneralLedgerMasterQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("To Date must be on or after From Date.");
    }
}
