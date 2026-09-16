using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Inventory.Queries.ProductBatchReport;

public sealed class ProductBatchReportQueryValidator : AbstractValidator<ProductBatchReportQuery>
{
    public ProductBatchReportQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.GroupBy).IsInEnum();

        // Phase 34b's uniform-sweep finding: asking one question of every paginated list turned up
        // two queries with no validator at all. A period that runs backwards is the cheapest
        // instance of that question, and a 400 naming the field beats an empty report.
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("The period's To date cannot be before its From date.");
    }
}
