using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.ListJournalVouchers;

public sealed class ListJournalVouchersQueryValidator : AbstractValidator<ListJournalVouchersQuery>
{
    public ListJournalVouchersQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
