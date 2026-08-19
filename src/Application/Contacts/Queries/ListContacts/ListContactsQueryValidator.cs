using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Contacts.Queries.ListContacts;

public sealed class ListContactsQueryValidator : AbstractValidator<ListContactsQuery>
{
    public ListContactsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
