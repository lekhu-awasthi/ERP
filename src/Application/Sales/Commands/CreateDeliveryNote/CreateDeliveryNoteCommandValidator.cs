using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Validation;
using ErpApp.Domain.Common;
using FluentValidation;

namespace ErpApp.Application.Sales.Commands.CreateDeliveryNote;

public sealed class CreateDeliveryNoteCommandValidator : AbstractValidator<CreateDeliveryNoteCommand>
{
    public CreateDeliveryNoteCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ExpectedDeliveryDate).NotEmpty();
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.TrackingNo).MaximumLength(100);
        RuleFor(x => x.ShippingAddress).MaximumLength(500);
        RuleFor(x => x.Terms).RichText("Terms and conditions");
        RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);

        RuleFor(x => x.ReferrerType)
            .Must(t => t is null or DocumentType.SalesOrder)
            .WithMessage("A delivery note can only be raised against a Sales Order.");
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
