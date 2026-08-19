using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Purchasing.Queries.ListDebitNotes;

public sealed class ListDebitNotesQueryValidator : AbstractValidator<ListDebitNotesQuery>
{
    public ListDebitNotesQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
