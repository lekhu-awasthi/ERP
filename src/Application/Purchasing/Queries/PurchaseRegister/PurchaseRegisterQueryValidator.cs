using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Purchasing.Queries.PurchaseRegister;

public sealed class PurchaseRegisterQueryValidator : AbstractValidator<PurchaseRegisterQuery>
{
    public PurchaseRegisterQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
