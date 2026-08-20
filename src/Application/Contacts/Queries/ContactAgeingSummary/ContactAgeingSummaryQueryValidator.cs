using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Contacts.Queries.ContactAgeingSummary;

public sealed class ContactAgeingSummaryQueryValidator : AbstractValidator<ContactAgeingSummaryQuery>
{
    public ContactAgeingSummaryQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
