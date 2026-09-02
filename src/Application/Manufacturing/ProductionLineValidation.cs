using System.Linq.Expressions;
using FluentValidation;

namespace ErpApp.Application.Manufacturing;

/// <summary>
/// The line-shape rules every manufacturing validator repeats. Mirrors
/// PagingValidation.ValidatePaging's extension-method shape.
///
/// <para><b>The parameters are <see cref="Expression{TDelegate}"/>, not <c>Func</c>, and that is
/// load-bearing.</b> FluentValidation derives a rule's property name by walking the expression
/// tree; hand it a compiled delegate and it throws <c>InvalidOperationException: Could not infer
/// property name for expression</c> the first time the rule actually runs. Nothing catches that at
/// compile time, and no handler unit test reaches it either -- tests call handlers directly, so
/// ValidationBehavior never executes. It surfaces only as a 500 against the real API, which is
/// where this phase found it. Same shape as phase-9 bug #1's captured-Func-in-Where.</para>
/// </summary>
internal static class ProductionLineValidation
{
    public static void ValidateProductionLines<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, IEnumerable<ProductionRawMaterialLineInput>>> rawMaterials,
        Expression<Func<T, IEnumerable<ProductionByProductLineInput>>> byProducts,
        Expression<Func<T, IEnumerable<ProductionExpenseLineInput>>> expenses)
    {
        validator.RuleForEach(rawMaterials).ChildRules(line =>
        {
            line.RuleFor(x => x.ProductId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
        });

        validator.RuleForEach(byProducts).ChildRules(line =>
        {
            line.RuleFor(x => x.ProductId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.CostAllocationPct).GreaterThanOrEqualTo(0).LessThan(100);
        });

        validator.RuleForEach(expenses).ChildRules(line =>
        {
            line.RuleFor(x => x.CostTermId).NotEmpty();
            line.RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
        });
    }
}
