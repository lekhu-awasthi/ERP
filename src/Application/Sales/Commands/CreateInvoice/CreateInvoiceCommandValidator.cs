using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Validation;
using FluentValidation;
using ErpApp.Application.Inventory.Stock;

namespace ErpApp.Application.Sales.Commands.CreateInvoice;

public sealed class CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Reference).MaximumLength(200);

        // Phase 39: Terms is rich text and had no rule at all before this phase.
        RuleFor(x => x.Terms).RichText("Terms and conditions");
        RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);
        RuleFor(x => x.Lines).NotNull();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ProductId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.Rate).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x.VatRate).IsInEnum();
            line.RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);
            line.BatchNoRule(x => x.BatchNo);
            line.SerialNumberRules(x => x.SerialNumbers);
        });

        this.AddCurrencyRules(x => x.CurrencyCode, x => x.ExchangeRate);

    }
}
