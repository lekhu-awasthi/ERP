using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Inventory.Commands.CreateOrUpdateOpeningStockLine;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Inventory;

public class CreateOrUpdateOpeningStockLineCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_a_real_fifo_layer_dated_at_the_accounting_start_date()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, productId, warehouseId) = await SeedAsync(db);
        var handler = new CreateOrUpdateOpeningStockLineCommandHandler(db, new StockLedgerService(db));

        var result = await handler.Handle(
            new CreateOrUpdateOpeningStockLineCommand(organizationId, productId, warehouseId, 50m, 20m), CancellationToken.None);

        Assert.Equal(50m, result.Quantity);
        var available = await new StockLedgerService(db).GetAvailableQuantityAsync(organizationId, productId, warehouseId, CancellationToken.None);
        Assert.Equal(50m, available);
    }

    [Fact]
    public async Task Handle_reverses_and_reincrements_when_correcting_an_existing_line()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, productId, warehouseId) = await SeedAsync(db);
        var stockLedger = new StockLedgerService(db);
        var handler = new CreateOrUpdateOpeningStockLineCommandHandler(db, stockLedger);
        await handler.Handle(
            new CreateOrUpdateOpeningStockLineCommand(organizationId, productId, warehouseId, 50m, 20m), CancellationToken.None);

        await handler.Handle(
            new CreateOrUpdateOpeningStockLineCommand(organizationId, productId, warehouseId, 80m, 25m), CancellationToken.None);

        var available = await stockLedger.GetAvailableQuantityAsync(organizationId, productId, warehouseId, CancellationToken.None);
        Assert.Equal(80m, available);
    }

    [Fact]
    public async Task Handle_throws_when_the_original_layer_has_already_been_partly_consumed()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, productId, warehouseId) = await SeedAsync(db);
        var stockLedger = new StockLedgerService(db);
        var handler = new CreateOrUpdateOpeningStockLineCommandHandler(db, stockLedger);
        await handler.Handle(
            new CreateOrUpdateOpeningStockLineCommand(organizationId, productId, warehouseId, 50m, 20m), CancellationToken.None);
        await stockLedger.ConsumeAsync(
            organizationId, productId, warehouseId, 10m, Domain.Common.DocumentType.Invoice, Guid.NewGuid(),
            new DateOnly(2026, 6, 1), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateOrUpdateOpeningStockLineCommand(organizationId, productId, warehouseId, 80m, 25m), CancellationToken.None));
    }

    private static async Task<(Guid OrganizationId, Guid ProductId, Guid WarehouseId)> SeedAsync(Application.Common.Persistence.IAppDbContext db)
    {
        var organization = Organization.Create(
            "Test Org", "Retail", null, new DateOnly(2026, 1, 1), false, "test-org", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(CancellationToken.None);
        var organizationId = organization.Id;

        var numberGenerator = new FakeDocumentNumberGenerator();
        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);
        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "General", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Piece", "pc"), CancellationToken.None);
        var product = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Goods, "Widget", category.Id, unit.Id, null, true, 150m, 100m,
                VatRate.NoVat, 0, true),
            CancellationToken.None);

        return (organizationId, product.Id, warehouse.Id);
    }
}
