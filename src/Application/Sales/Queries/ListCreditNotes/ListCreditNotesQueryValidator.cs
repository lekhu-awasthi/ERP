using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Sales.Queries.ListCreditNotes;

public sealed class ListCreditNotesQueryValidator : AbstractValidator<ListCreditNotesQuery>
{
    public ListCreditNotesQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
