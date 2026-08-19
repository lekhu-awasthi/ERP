using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.ListCashTransfers;

public sealed class ListCashTransfersQueryValidator : AbstractValidator<ListCashTransfersQuery>
{
    public ListCashTransfersQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
