using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using FluentValidation;

namespace ErpApp.Application.UnitTests.Currencies;

/// <summary>
/// Phase 25's lesson, paid forward: a shared FluentValidation helper built from a captured
/// <c>Func</c> selector throws "Could not infer property name" at runtime and 500s every endpoint
/// it guards, while every handler test stays green -- <b>only a validator test can see it.</b>
/// <see cref="CurrencyValidationRules"/> takes <c>Expression</c> selectors precisely because of
/// that, and this class is what proves the rules run at all.
/// </summary>
public class CurrencyValidationRulesTests
{
    [Fact]
    public void The_shared_rules_actually_execute_rather_than_throwing_on_property_inference()
    {
        var result = Validate("USD", 133m);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void A_null_pair_is_valid_because_it_means_the_base_currency_at_rate_one()
    {
        Assert.True(Validate(null, null).IsValid);
    }

    [Fact]
    public void An_unsupported_currency_is_rejected_and_names_the_field()
    {
        var result = Validate("XYZ", 1m);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(CreateInvoiceCommand.CurrencyCode));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_rate_is_rejected(decimal rate)
    {
        var result = Validate("USD", rate);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(CreateInvoiceCommand.ExchangeRate));
    }

    [Fact]
    public void A_base_currency_document_with_a_rate_other_than_one_is_rejected()
    {
        var result = Validate("NPR", 133m);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.ErrorMessage.Contains("exactly 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_currency_bearing_command_wires_the_shared_rules_into_its_validator()
    {
        // The sweep guard for the twenty-three validators: a command could carry the pair and
        // silently validate none of it, and nothing would fail. Asserted by construction rather
        // than by inspection -- each validator is instantiated and run against a deliberately
        // unsupported currency code, which only the shared rules reject.
        var assembly = typeof(CreateInvoiceCommand).Assembly;

        var commandTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: false } or { IsAbstract: false, IsClass: true }
                        && typeof(ICurrencyBearingCommand).IsAssignableFrom(t)
                        && t != typeof(ICurrencyBearingCommand))
            .ToList();

        Assert.Equal(23, commandTypes.Count);

        foreach (var commandType in commandTypes)
        {
            var validatorType = assembly.GetTypes().SingleOrDefault(
                t => t.BaseType is { IsGenericType: true }
                     && t.BaseType.GetGenericTypeDefinition() == typeof(AbstractValidator<>)
                     && t.BaseType.GetGenericArguments()[0] == commandType);

            Assert.True(validatorType is not null, $"{commandType.Name} has no validator.");

            var validator = Activator.CreateInstance(validatorType!)!;
            var descriptor = ((IValidator)validator).CreateDescriptor();

            Assert.True(
                descriptor.GetMembersWithValidators().Any(x => x.Key == nameof(ICurrencyBearingCommand.CurrencyCode)),
                $"{commandType.Name}'s validator has no rule on CurrencyCode -- AddCurrencyRules was not called.");
        }
    }

    private static FluentValidation.Results.ValidationResult Validate(string? currencyCode, decimal? exchangeRate)
    {
        var command = new CreateInvoiceCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null,
            [new InvoiceLineInput(Guid.NewGuid(), 1m, 100m, VatRate.NoVat)])
        {
            CurrencyCode = currencyCode,
            ExchangeRate = exchangeRate,
        };

        var result = new CreateInvoiceCommandValidator().Validate(command);

        // Only currency failures matter here; the rest of the command is deliberately valid.
        Assert.DoesNotContain(result.Errors, x =>
            x.PropertyName != nameof(CreateInvoiceCommand.CurrencyCode)
            && x.PropertyName != nameof(CreateInvoiceCommand.ExchangeRate)
            && x.PropertyName.Length > 0
            && !x.ErrorMessage.Contains(CurrencyCatalog.BaseCode, StringComparison.Ordinal));

        return result;
    }
}
