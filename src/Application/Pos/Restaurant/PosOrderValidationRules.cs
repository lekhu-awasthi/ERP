using System.Linq.Expressions;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using FluentValidation;

namespace ErpApp.Application.Pos.Restaurant;

/// <summary>
/// Phase 64 -- the field rules every order request shares, so a bad quantity or an over-long note is a
/// 400 naming the field rather than the Domain's backstop 500 (phase 39's rule).
///
/// <para><b>Every rule on a list is made here, from the one expression.</b> FluentValidation caches one
/// compiled accessor per member, so <c>RuleFor(x =&gt; x.Items)</c> typed as
/// <c>IEnumerable&lt;T&gt;</c> here and again as <c>IReadOnlyList&lt;T&gt;</c> in a validator throws
/// <c>InvalidCastException</c> when the validator is built -- a 500 on every endpoint it guards, which
/// no handler test sees (phase 25's captured-<c>Func</c> gotcha, through another door; phase-64-status.md
/// bug 1). So the count rules are parameters, not a second <c>RuleFor</c> in the caller.</para>
/// </summary>
public static class PosOrderValidationRules
{
    /// <summary>More of one thing than any table orders; a bound, not a rule.</summary>
    public const decimal MaxQuantity = 10_000m;

    public static IRuleBuilderOptions<T, decimal> PosOrderQuantity<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0m)
            .LessThanOrEqualTo(MaxQuantity)
            .Must(x => decimal.Round(x, UnitConversion.QuantityScale) == x)
            .WithMessage($"A quantity has at most {UnitConversion.QuantityScale} decimal places.");

    /// <param name="requiredMessage">When set, an empty list is refused with this message.</param>
    public static void ValidateItems<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, IEnumerable<PosOrderItemInput>>> items,
        string? requiredMessage = null)
    {
        var rule = validator.RuleFor(items)
            .NotNull()
            .Must(x => x is null || x.Count() <= PosOrder.MaxLines)
            .WithMessage($"An order holds at most {PosOrder.MaxLines} lines.");

        if (requiredMessage is not null)
        {
            rule.Must(x => x is null || x.Any()).WithMessage(requiredMessage);
        }

        validator.RuleForEach(items).ChildRules(item =>
        {
            item.RuleFor(x => x.ProductId).NotEmpty();
            item.RuleFor(x => x.Quantity).PosOrderQuantity();
            item.RuleFor(x => x.Note).MaximumLength(PosOrder.MaxNoteLength);
        });
    }

    /// <param name="requiredMessage">When set, an empty list is refused with this message.</param>
    public static void ValidateLineQuantities<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, IEnumerable<PosOrderLineQuantityInput>>> lines,
        string? requiredMessage = null)
    {
        var rule = validator.RuleFor(lines)
            .NotNull()
            .Must(x => x is null || x.Select(l => l.LineId).Distinct().Count() == x.Count())
            .WithMessage("A line is named twice.");

        if (requiredMessage is not null)
        {
            rule.Must(x => x is null || x.Any()).WithMessage(requiredMessage);
        }

        validator.RuleForEach(lines).ChildRules(line =>
        {
            line.RuleFor(x => x.LineId).NotEmpty();
            line.RuleFor(x => x.Quantity).PosOrderQuantity();
        });
    }

    public static IRuleBuilderOptions<T, string> PosOrderReason<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(x => !string.IsNullOrWhiteSpace(x))
            .WithMessage("A discard needs a reason.")
            .MaximumLength(PosOrder.MaxReasonLength);
}
