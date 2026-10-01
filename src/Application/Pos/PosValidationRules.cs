using ErpApp.Domain.Pos;
using FluentValidation;

namespace ErpApp.Application.Pos;

/// <summary>
/// Phase 61 -- the till's money rules as validator rules, so a bad amount is a 400 naming the field
/// rather than the Domain's backstop surfacing as a 500 (phase 39). Extension methods over a rule
/// builder, never a captured selector (phase 25).
/// </summary>
public static class PosValidationRules
{
    private const string WholePaisa = "in whole paisa (at most 2 decimal places)";

    public static IRuleBuilderOptions<T, decimal> PosCashAmount<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.Must(x => x >= 0m && decimal.Round(x, 2) == x)
            .WithMessage($"'{{PropertyName}}' must be zero or more, {WholePaisa}.");

    public static IRuleBuilderOptions<T, decimal?> PosCashAmount<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.Must(x => x is null || (x >= 0m && decimal.Round(x.Value, 2) == x))
            .WithMessage($"'{{PropertyName}}' must be zero or more, {WholePaisa}.");

    public static IRuleBuilderOptions<T, decimal> PosPositiveCashAmount<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.Must(x => x > 0m && decimal.Round(x, 2) == x)
            .WithMessage($"'{{PropertyName}}' must be more than zero, {WholePaisa}.");

    public static IRuleBuilderOptions<T, IReadOnlyList<DenominationCount>?> PosDenominations<T>(
        this IRuleBuilder<T, IReadOnlyList<DenominationCount>?> rule) =>
        rule.Must(x => x is null
                || (x.Count <= PosLocationSettings.MaxDenominations
                    && x.All(r => r is not null && r.Value > 0 && r.Count >= 0)
                    && x.Select(r => r.Value).Distinct().Count() == x.Count))
            .WithMessage(
                $"'{{PropertyName}}' lists each note or coin once, at a positive face value and a count of zero "
                + $"or more, and at most {PosLocationSettings.MaxDenominations} of them.");
}
