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

        // Phase 66 -- a channel names a kind of sale; Purchase By Item has none to filter on.
        this.RuleFor(x => x.Channel)
            .IsInEnum()
            .Must((query, channel) => channel is null || query.Side == TradeSide.Sales)
            .WithMessage("Channel applies to sales only.");
    }
}
