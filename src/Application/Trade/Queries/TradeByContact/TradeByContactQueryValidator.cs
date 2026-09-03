using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Trade.Queries.TradeByContact;

public sealed class TradeByContactQueryValidator : AbstractValidator<TradeByContactQuery>
{
    public TradeByContactQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        this.RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("ToDate must not be earlier than FromDate.");
    }
}
