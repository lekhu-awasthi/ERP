using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Trade.Queries.TradeByItem;

public sealed class TradeByItemQueryValidator : AbstractValidator<TradeByItemQuery>
{
    public TradeByItemQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        this.RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("ToDate must not be earlier than FromDate.");
    }
}
