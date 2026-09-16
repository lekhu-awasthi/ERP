using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Inventory.Queries.ProductSerialReport;

public sealed class ProductSerialReportQueryValidator : AbstractValidator<ProductSerialReportQuery>
{
    public ProductSerialReportQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.GroupBy).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();

        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("The period's To date cannot be before its From date.");
    }
}
