using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Sales.Queries.SalesRegister;

public sealed class SalesRegisterQueryValidator : AbstractValidator<SalesRegisterQuery>
{
    public SalesRegisterQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
