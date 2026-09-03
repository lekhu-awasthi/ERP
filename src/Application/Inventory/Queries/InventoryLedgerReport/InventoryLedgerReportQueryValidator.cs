using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Inventory.Queries.InventoryLedgerReport;

public sealed class InventoryLedgerReportQueryValidator : AbstractValidator<InventoryLedgerReportQuery>
{
    public InventoryLedgerReportQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        // A kardex is a per-product document; the live screen refuses to generate without one.
        // Enforced here so the caller gets a 400 naming the field rather than an empty report that
        // looks like "this product had no movement".
        RuleFor(x => x.ProductId).NotEmpty();

        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("'To Date' must not be earlier than 'From Date'.");
    }
}
