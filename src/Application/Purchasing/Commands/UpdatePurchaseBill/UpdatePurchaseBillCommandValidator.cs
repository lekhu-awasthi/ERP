using ErpApp.Application.Common.Currencies;
using FluentValidation;

namespace ErpApp.Application.Purchasing.Commands.UpdatePurchaseBill;

public sealed class UpdatePurchaseBillCommandValidator : AbstractValidator<UpdatePurchaseBillCommand>
{
    public UpdatePurchaseBillCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.SupplierInvoiceReference).MaximumLength(100);

        RuleFor(x => x.ImportCountry).NotEmpty().MaximumLength(100).When(x => x.IsImport);
        RuleFor(x => x.ImportDate).NotNull().When(x => x.IsImport);
        RuleFor(x => x.ImportDocumentNo).NotEmpty().MaximumLength(100).When(x => x.IsImport);

        RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);
        RuleFor(x => x.Lines).NotNull();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ProductId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.Rate).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x.VatRate).IsInEnum();
            line.RuleFor(x => x.ExpenditureClassification).IsInEnum();
            line.RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);
        });


        // Phase 29 (FR-6.15) -- the Additional Cost section. A null list is "no additional cost".
        RuleForEach(x => x.AdditionalCosts).ChildRules(cost =>
        {
            cost.RuleFor(x => x.CostTermId).NotEmpty();
            cost.RuleFor(x => x.Method).IsInEnum();
            cost.RuleFor(x => x.Amount).GreaterThan(0);
        });

        this.AddCurrencyRules(x => x.CurrencyCode, x => x.ExchangeRate);

    }
}
