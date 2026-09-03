using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Contacts.Queries.ContactBalanceSummary;

public sealed class ContactBalanceSummaryQueryValidator : AbstractValidator<ContactBalanceSummaryQuery>
{
    public ContactBalanceSummaryQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        this.RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("ToDate must not be earlier than FromDate.");
    }
}
