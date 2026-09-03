using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Contacts.Queries.DocumentAge;

public sealed class DocumentAgeQueryValidator : AbstractValidator<DocumentAgeQuery>
{
    public DocumentAgeQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        this.RuleFor(x => x.AsOfDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("AsOfDate must not be earlier than FromDate.");
    }
}
