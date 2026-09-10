using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Contacts.Queries.ListContacts;

public sealed class ListContactsQueryValidator : AbstractValidator<ListContactsQuery>
{
    public ListContactsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        this.ValidateSearch(x => x.Search);
    }
}
