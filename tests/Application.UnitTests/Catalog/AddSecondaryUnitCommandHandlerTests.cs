using ErpApp.Application.Catalog.Commands.AddSecondaryUnit;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Catalog;

public class AddSecondaryUnitCommandHandlerTests
{
    [Fact]
    public async Task Handle_appends_secondary_unit_to_the_product()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var category = ProductCategory.Create(organizationId, "Electronics", null);
        var primaryUnit = UnitOfMeasurement.Create(organizationId, "Piece", "pc");
        var secondaryUnit = UnitOfMeasurement.Create(organizationId, "Box", "box");
        var product = Product.Create(
            organizationId, ProductType.Goods, "Widget", "PRD-0001", category.Id, primaryUnit.Id, null, true,
            100m, 80m, VatRate.NoVat, 0, true);
        db.ProductCategories.Add(category);
        db.UnitsOfMeasurement.AddRange(primaryUnit, secondaryUnit);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var handler = new AddSecondaryUnitCommandHandler(db);
        var result = await handler.Handle(
            new AddSecondaryUnitCommand(organizationId, product.Id, secondaryUnit.Id, 12m, 1000m, 900m),
            CancellationToken.None);

        var persisted = await db.ProductSecondaryUnits.SingleAsync(x => x.Id == result.Id);
        Assert.Equal(product.Id, persisted.ProductId);
        Assert.Equal(secondaryUnit.Id, persisted.UnitId);
        Assert.Equal(12m, persisted.ConversionRate);
    }

    [Fact]
    public async Task Handle_throws_not_found_when_product_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var handler = new AddSecondaryUnitCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new AddSecondaryUnitCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, 0m, 0m),
            CancellationToken.None));
    }
}
