using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Contacts.Queries.ContactStatement;

public sealed class ContactStatementQueryValidator : AbstractValidator<ContactStatementQuery>
{
    public ContactStatementQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
