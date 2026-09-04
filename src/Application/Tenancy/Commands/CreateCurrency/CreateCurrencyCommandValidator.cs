using ErpApp.Domain.Common;
using FluentValidation;

namespace ErpApp.Application.Tenancy.Commands.CreateCurrency;

public sealed class CreateCurrencyCommandValidator : AbstractValidator<CreateCurrencyCommand>
{
    public CreateCurrencyCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .Must(x => CurrencyCatalog.Contains(x))
            .WithMessage("'{PropertyValue}' is not a currency this product supports.");

        RuleFor(x => x.Name).MaximumLength(60);
        RuleFor(x => x.Symbol).MaximumLength(10);
    }
}
