using System.Linq.Expressions;
using ErpApp.Domain.Common;
using FluentValidation;

namespace ErpApp.Application.Common.Currencies;

/// <summary>
/// The shared FluentValidation rules for a command's currency pair, so all twenty-three of them
/// reject the same inputs with the same messages.
///
/// <para>Takes <see cref="Expression{TDelegate}"/> selectors, never a compiled
/// <see cref="Func{T, TResult}"/>. Phase 25's lesson, restated: a rule built from a captured
/// <c>Func</c> throws "Could not infer property name" at *runtime*, 500ing every endpoint it
/// guards, and no handler test can see it -- only a validator test can. Hence
/// <c>CurrencyValidationRulesTests</c>.</para>
/// </summary>
public static class CurrencyValidationRules
{
    public static void AddCurrencyRules<TCommand>(
        this AbstractValidator<TCommand> validator,
        Expression<Func<TCommand, string?>> currencyCode,
        Expression<Func<TCommand, decimal?>> exchangeRate)
        where TCommand : ICurrencyBearingCommand
    {
        validator.RuleFor(currencyCode)
            .Must(x => x is null || CurrencyCatalog.Contains(x))
            .WithMessage("'{PropertyValue}' is not a currency this product supports.");

        validator.RuleFor(exchangeRate)
            .GreaterThan(0)
            .When(x => x.ExchangeRate is not null)
            .WithMessage("Exchange Rate must be greater than zero.");

        // The base-currency-implies-rate-one rule is also an aggregate invariant
        // (ExchangeRates.Validate). It is restated here so the caller gets a 400 naming the field
        // rather than a 500 from the Domain -- the same division of labour every other
        // validator/aggregate pair in this codebase uses.
        validator.RuleFor(x => x)
            .Must(x => x.ExchangeRate is null
                       || x.ExchangeRate == ExchangeRates.BaseRate
                       || !CurrencyCatalog.IsBase(x.CurrencyCode ?? CurrencyCatalog.BaseCode))
            .WithMessage($"A document in {CurrencyCatalog.BaseCode} must have an Exchange Rate of exactly 1.");
    }
}
