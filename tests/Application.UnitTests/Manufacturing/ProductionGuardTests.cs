using ErpApp.Application.Catalog.Commands.CreateProductVariant;
using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Catalog.Commands.SetProductVariantAttributes;
using ErpApp.Application.Catalog.Commands.CreateVariantAttribute;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Manufacturing;
using ErpApp.Application.Manufacturing.Commands.ApproveProductionOrder;
using ErpApp.Application.Manufacturing.Commands.CreateBillOfMaterials;
using ErpApp.Application.Manufacturing.Commands.CreateProductionJournal;
using ErpApp.Application.Manufacturing.Commands.CreateProductionOrder;
using ErpApp.Application.Manufacturing.Queries.GetProductionJournalConversionTemplate;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Manufacturing;

/// <summary>
/// The three rules a manufacturing document must not be able to break: converting an order twice
/// (phase-6 bug #4), naming a variant <i>parent</i> on a line (phase-24's sweep, whose guard test
/// explicitly names this phase as the failure it is watching for), and using a landed-cost term
/// where a production-cost term belongs.
/// </summary>
public class ProductionGuardTests
{
    private static readonly DateOnly RunDay = new(2026, 1, 25);

    [Fact]
    public async Task A_production_order_converts_to_a_journal_exactly_once()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);
        var orderId = await CreateAndApproveOrderAsync(db, seed);

        var first = await CreateJournalFromOrderAsync(db, seed, orderId);
        Assert.NotEqual(Guid.Empty, first);

        var error = await Assert.ThrowsAsync<ConflictException>(() => CreateJournalFromOrderAsync(db, seed, orderId));
        Assert.Contains("already been converted", error.Message, StringComparison.Ordinal);

        var order = await db.ProductionOrders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(ProductionOrderStatus.Converted, order.Status);
    }

    [Fact]
    public async Task The_conversion_template_refuses_an_order_that_has_already_been_converted()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);
        var orderId = await CreateAndApproveOrderAsync(db, seed);

        await CreateJournalFromOrderAsync(db, seed, orderId);

        await Assert.ThrowsAsync<ConflictException>(() =>
            new GetProductionJournalConversionTemplateQueryHandler(db).Handle(
                new GetProductionJournalConversionTemplateQuery(seed.OrganizationId, orderId), CancellationToken.None));
    }

    [Fact]
    public async Task A_variant_parent_is_refused_on_a_bill_of_materials_while_a_variant_child_is_accepted()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);
        var (parentId, childId) = await MakeVariantFamilyAsync(db, seed);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            new CreateBillOfMaterialsCommandHandler(db).Handle(
                new CreateBillOfMaterialsCommand(
                    seed.OrganizationId, seed.FinishedProductId, 10m, false, null,
                    [new ProductionRawMaterialLineInput(parentId, 2m)], [], []),
                CancellationToken.None));

        Assert.Contains("has variants", error.Message, StringComparison.Ordinal);

        // The child is an ordinary transactable product and goes through unchanged.
        var created = await new CreateBillOfMaterialsCommandHandler(db).Handle(
            new CreateBillOfMaterialsCommand(
                seed.OrganizationId, seed.FinishedProductId, 10m, false, null,
                [new ProductionRawMaterialLineInput(childId, 2m)], [], []),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Fact]
    public async Task A_variant_parent_is_refused_as_a_production_journals_finished_good_and_as_a_by_product()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);
        var (parentId, _) = await MakeVariantFamilyAsync(db, seed);

        await Assert.ThrowsAsync<ConflictException>(() =>
            new CreateProductionJournalCommandHandler(db).Handle(
                new CreateProductionJournalCommand(
                    seed.OrganizationId, RunDay, null, parentId, 10m, seed.WarehouseId, null, null, null, null,
                    [new ProductionRawMaterialLineInput(seed.RawProductId, 2m)], [], []),
                CancellationToken.None));

        await Assert.ThrowsAsync<ConflictException>(() =>
            new CreateProductionJournalCommandHandler(db).Handle(
                new CreateProductionJournalCommand(
                    seed.OrganizationId, RunDay, null, seed.FinishedProductId, 10m, seed.WarehouseId, null, null, null, null,
                    [new ProductionRawMaterialLineInput(seed.RawProductId, 2m)],
                    [new ProductionByProductLineInput(parentId, 10m, 1m)],
                    []),
                CancellationToken.None));
    }

    [Fact]
    public async Task A_service_product_cannot_be_manufactured_or_consumed()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            new CreateProductionJournalCommandHandler(db).Handle(
                new CreateProductionJournalCommand(
                    seed.OrganizationId, RunDay, null, seed.FinishedProductId, 10m, seed.WarehouseId, null, null, null, null,
                    [new ProductionRawMaterialLineInput(seed.ServiceProductId, 2m)], [], []),
                CancellationToken.None));

        Assert.Contains("Service products carry no stock", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_landed_cost_term_cannot_be_used_as_a_production_expense()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        // Phase 20c built CostTermCategory with exactly two members so this distinction is real
        // rather than a display grouping.
        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            new CreateBillOfMaterialsCommandHandler(db).Handle(
                new CreateBillOfMaterialsCommand(
                    seed.OrganizationId, seed.FinishedProductId, 10m, false, null,
                    [new ProductionRawMaterialLineInput(seed.RawProductId, 2m)],
                    [],
                    [new ProductionExpenseLineInput(seed.AdditionalCostTermId, 100m)]),
                CancellationToken.None));

        Assert.Contains("Production Cost terms", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_product_may_have_only_one_bill_of_materials()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        await new CreateBillOfMaterialsCommandHandler(db).Handle(
            new CreateBillOfMaterialsCommand(
                seed.OrganizationId, seed.FinishedProductId, 10m, false, null,
                [new ProductionRawMaterialLineInput(seed.RawProductId, 2m)], [], []),
            CancellationToken.None);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            new CreateBillOfMaterialsCommandHandler(db).Handle(
                new CreateBillOfMaterialsCommand(
                    seed.OrganizationId, seed.FinishedProductId, 20m, false, null,
                    [new ProductionRawMaterialLineInput(seed.RawProductId, 4m)], [], []),
                CancellationToken.None));

        Assert.Contains("already has a bill of materials", error.Message, StringComparison.Ordinal);
    }

    private static async Task<(Guid ParentId, Guid ChildId)> MakeVariantFamilyAsync(
        IAppDbContext db, ManufacturingSeed seed)
    {
        var attribute = await new CreateVariantAttributeCommandHandler(db).Handle(
            new CreateVariantAttributeCommand(seed.OrganizationId, "Size", ["Small", "Large"]), CancellationToken.None);

        var optionIds = await db.VariantAttributeOptions
            .Where(x => x.VariantAttributeId == attribute.Id)
            .Select(x => x.Id)
            .ToListAsync();

        await new SetProductVariantAttributesCommandHandler(db).Handle(
            new SetProductVariantAttributesCommand(
                seed.OrganizationId,
                seed.SecondRawProductId,
                [.. optionIds.Select(id => new VariantCombinationInput(attribute.Id, id))]),
            CancellationToken.None);

        var child = await new CreateProductVariantCommandHandler(db, seed.NumberGenerator).Handle(
            new CreateProductVariantCommand(
                seed.OrganizationId, seed.SecondRawProductId,
                [new VariantCombinationInput(attribute.Id, optionIds[0])], null, null, null, 150m, 100m),
            CancellationToken.None);

        return (seed.SecondRawProductId, child.Id);
    }

    private static async Task<Guid> CreateAndApproveOrderAsync(IAppDbContext db, ManufacturingSeed seed)
    {
        var created = await new CreateProductionOrderCommandHandler(db).Handle(
            new CreateProductionOrderCommand(
                seed.OrganizationId, RunDay, null, seed.FinishedProductId, 10m, null, null,
                [new ProductionRawMaterialLineInput(seed.RawProductId, 20m)], [], []),
            CancellationToken.None);

        await new ApproveProductionOrderCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(new ApproveProductionOrderCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return created.Id;
    }

    private static async Task<Guid> CreateJournalFromOrderAsync(IAppDbContext db, ManufacturingSeed seed, Guid orderId)
    {
        var created = await new CreateProductionJournalCommandHandler(db).Handle(
            new CreateProductionJournalCommand(
                seed.OrganizationId, RunDay, null, seed.FinishedProductId, 10m, seed.WarehouseId, null, null,
                DocumentType.ProductionOrder, orderId,
                [new ProductionRawMaterialLineInput(seed.RawProductId, 20m)], [], []),
            CancellationToken.None);

        return created.Id;
    }
}
