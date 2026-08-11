using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Catalog;

public class CreateProductCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_product_with_a_generated_code_and_fifo_valuation()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var category = ProductCategory.Create(organizationId, "Electronics", null);
        var unit = UnitOfMeasurement.Create(organizationId, "Piece", "pc");
        db.ProductCategories.Add(category);
        db.UnitsOfMeasurement.Add(unit);
        await db.SaveChangesAsync();

        var handler = new CreateProductCommandHandler(db, new FakeDocumentNumberGenerator());

        var result = await handler.Handle(
            new CreateProductCommand(
                organizationId, ProductType.Goods, "Widget", category.Id, unit.Id, null, true, 150m, 100m,
                VatRate.ThirteenPercentVat, 5, true),
            CancellationToken.None);

        var product = await db.Products.SingleAsync(x => x.Id == result.Id);
        Assert.False(string.IsNullOrWhiteSpace(product.Code));
        Assert.Equal(ValuationMethod.Fifo, product.ValuationMethod);
        Assert.True(product.IsActive);
    }

    [Fact]
    public async Task Handle_throws_not_found_when_category_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var unit = UnitOfMeasurement.Create(organizationId, "Piece", "pc");
        db.UnitsOfMeasurement.Add(unit);
        await db.SaveChangesAsync();

        var handler = new CreateProductCommandHandler(db, new FakeDocumentNumberGenerator());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateProductCommand(
                organizationId, ProductType.Goods, "Widget", Guid.NewGuid(), unit.Id, null, true, 0m, 0m,
                VatRate.NoVat, 0, true),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_not_found_when_primary_unit_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var category = ProductCategory.Create(organizationId, "Electronics", null);
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();

        var handler = new CreateProductCommandHandler(db, new FakeDocumentNumberGenerator());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateProductCommand(
                organizationId, ProductType.Goods, "Widget", category.Id, Guid.NewGuid(), null, true, 0m, 0m,
                VatRate.NoVat, 0, true),
            CancellationToken.None));
    }
}
