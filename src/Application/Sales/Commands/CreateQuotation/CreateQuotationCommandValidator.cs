using ErpApp.Application.Common.Currencies;
using FluentValidation;

namespace ErpApp.Application.Sales.Commands.CreateQuotation;

public sealed class CreateQuotationCommandValidator : AbstractValidator<CreateQuotationCommand>
{
    public CreateQuotationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);
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
