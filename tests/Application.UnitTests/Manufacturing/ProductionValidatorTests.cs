using ErpApp.Application.Manufacturing;
using ErpApp.Application.Manufacturing.Commands.CreateBillOfMaterials;
using ErpApp.Application.Manufacturing.Commands.CreateProductionJournal;
using ErpApp.Application.Manufacturing.Commands.CreateProductionOrder;
using ErpApp.Application.Manufacturing.Commands.UpdateBillOfMaterials;
using ErpApp.Application.Manufacturing.Commands.UpdateProductionJournal;
using ErpApp.Application.Manufacturing.Commands.UpdateProductionOrder;
using FluentValidation;
using FluentValidation.Results;

namespace ErpApp.Application.UnitTests.Manufacturing;

/// <summary>
/// <b>These exist because of a bug this phase shipped and then found by hand.</b>
///
/// <para>The shared line-shape rules started life taking plain <c>Func</c> selectors.
/// It compiled, every handler test passed, and every one of the six manufacturing endpoints
/// returned a <b>500</b> the first time it was called against the real API:
/// FluentValidation derives a rule's property name by walking an expression tree, and a compiled
/// delegate has none, so it throws <i>Could not infer property name for expression</i> at
/// validation time. Handler tests never see it -- they call handlers directly, so
/// ValidationBehavior never runs.</para>
///
/// <para>So these tests do the one thing those could not: <b>actually execute each validator</b>.
/// A rule that cannot name its property fails here rather than in production.</para>
/// </summary>
public class ProductionValidatorTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 1, 25);

    private static IReadOnlyList<ProductionRawMaterialLineInput> Raw(decimal quantity = 5m) =>
        [new ProductionRawMaterialLineInput(Id, quantity)];

    private static IReadOnlyList<ProductionByProductLineInput> ByProducts(decimal pct = 10m) =>
        [new ProductionByProductLineInput(Id, pct, 2m)];

    private static IReadOnlyList<ProductionExpenseLineInput> Expenses(decimal amount = 100m) =>
        [new ProductionExpenseLineInput(Id, amount)];

    private static CreateBillOfMaterialsCommand Bom(
        IReadOnlyList<ProductionRawMaterialLineInput>? raw = null,
        IReadOnlyList<ProductionByProductLineInput>? byProducts = null) =>
        new(Id, Id, 10m, false, null, raw ?? Raw(), byProducts ?? ByProducts(), Expenses());

    private static CreateProductionOrderCommand Order(IReadOnlyList<ProductionRawMaterialLineInput>? raw = null) =>
        new(Id, Date, null, Id, 10m, null, null, raw ?? Raw(), ByProducts(), Expenses());

    private static CreateProductionJournalCommand Journal(IReadOnlyList<ProductionRawMaterialLineInput>? raw = null) =>
        new(Id, Date, null, Id, 10m, Id, null, null, null, null, raw ?? Raw(), ByProducts(), Expenses());

    private static ValidationResult Run<T>(AbstractValidator<T> validator, T command) => validator.Validate(command);

    [Fact]
    public void Every_manufacturing_validator_runs_without_failing_to_name_its_own_rules()
    {
        // The regression itself: each of these threw InvalidOperationException before the fix.
        Assert.True(Run(new CreateBillOfMaterialsCommandValidator(), Bom()).IsValid);
        Assert.True(Run(new CreateProductionOrderCommandValidator(), Order()).IsValid);
        Assert.True(Run(new CreateProductionJournalCommandValidator(), Journal()).IsValid);

        Assert.True(
            Run(
                new UpdateBillOfMaterialsCommandValidator(),
                new UpdateBillOfMaterialsCommand(Id, Id, Id, 10m, false, null, true, Raw(), ByProducts(), Expenses()))
                .IsValid);

        Assert.True(
            Run(
                new UpdateProductionOrderCommandValidator(),
                new UpdateProductionOrderCommand(Id, Id, Date, null, Id, 10m, null, null, Raw(), ByProducts(), Expenses()))
                .IsValid);

        Assert.True(
            Run(
                new UpdateProductionJournalCommandValidator(),
                new UpdateProductionJournalCommand(
                    Id, Id, Date, null, Id, 10m, Id, null, null, Raw(), ByProducts(), Expenses()))
                .IsValid);
    }

    [Fact]
    public void A_line_rule_names_the_property_it_failed_on()
    {
        // Not just "it did not throw": the message has to identify which collection and which
        // element, which is the whole reason the expression tree matters.
        var result = Run(new CreateProductionJournalCommandValidator(), Journal(Raw(quantity: 0m)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("RawMaterials", StringComparison.Ordinal));
    }

    [Fact]
    public void A_by_product_percentage_of_one_hundred_or_more_is_rejected_before_the_handler()
    {
        var result = Run(
            new CreateBillOfMaterialsCommandValidator(),
            Bom(byProducts: [new ProductionByProductLineInput(Id, 100m, 1m)]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("ByProducts", StringComparison.Ordinal));
    }

    [Fact]
    public void A_document_with_no_raw_materials_is_rejected_before_the_handler()
    {
        Assert.False(Run(new CreateBillOfMaterialsCommandValidator(), Bom(raw: [])).IsValid);
        Assert.False(Run(new CreateProductionOrderCommandValidator(), Order(raw: [])).IsValid);
        Assert.False(Run(new CreateProductionJournalCommandValidator(), Journal(raw: [])).IsValid);
    }
}
