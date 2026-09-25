using ErpApp.Application.Common.Currencies;
using ErpApp.Domain.Common;
using FluentValidation;

namespace ErpApp.Application.Purchasing.Commands.CreateGoodsReceivedNote;

public sealed class CreateGoodsReceivedNoteCommandValidator : AbstractValidator<CreateGoodsReceivedNoteCommand>
{
    public CreateGoodsReceivedNoteCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.TrackingNo).MaximumLength(100);
        RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);

        // A GRN is received against a Purchase Order or against nothing -- the live reference picker
        // queries purchase-orders and nothing else.
        RuleFor(x => x.ReferrerType)
            .Must(t => t is null or DocumentType.PurchaseOrder)
            .WithMessage("A goods received note can only be received against a Purchase Order.");
        RuleFor(x => x.ReferrerId).NotEmpty().When(x => x.ReferrerType is not null);

        RuleFor(x => x.Lines).NotNull();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ProductId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.Rate).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x.VatRate).IsInEnum();
            line.RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);
        });

        this.AddCurrencyRules(x => x.CurrencyCode, x => x.ExchangeRate);
    }
}
