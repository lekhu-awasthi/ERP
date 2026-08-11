using ErpApp.Application.Catalog.Commands.UpdateProduct;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;

namespace ErpApp.Application.UnitTests.Catalog;

public class UpdateProductCommandHandlerTests
{
    [Fact]
    public async Task Handle_updates_editable_fields_but_not_type_or_code()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var category = ProductCategory.Create(organizationId, "Electronics", null);
        var unit = UnitOfMeasurement.Create(organizationId, "Piece", "pc");
        var product = Product.Create(
            organizationId, ProductType.Goods, "Widget", "PRD-0001", category.Id, unit.Id, null, true, 100m, 80m,
            VatRate.NoVat, 0, true);
        db.ProductCategories.Add(category);
        db.UnitsOfMeasurement.Add(unit);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var handler = new UpdateProductCommandHandler(db);
        await handler.Handle(
            new UpdateProductCommand(
                organizationId, product.Id, "Widget Deluxe", category.Id, unit.Id, "1234.56", false, 200m, 150m,
                VatRate.ThirteenPercentVat, 10, false, false),
            CancellationToken.None);

        Assert.Equal("Widget Deluxe", product.Name);
        Assert.Equal(200m, product.SellingPrice);
        Assert.False(product.IsActive);
        Assert.Equal(ProductType.Goods, product.Type);
        Assert.Equal("PRD-0001", product.Code);
    }

    [Fact]
    public async Task Handle_throws_not_found_when_product_belongs_to_a_different_organization()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var category = ProductCategory.Create(organizationId, "Electronics", null);
        var unit = UnitOfMeasurement.Create(organizationId, "Piece", "pc");
        var product = Product.Create(
            organizationId, ProductType.Goods, "Widget", "PRD-0001", category.Id, unit.Id, null, true, 100m, 80m,
            VatRate.NoVat, 0, true);
        db.ProductCategories.Add(category);
        db.UnitsOfMeasurement.Add(unit);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var handler = new UpdateProductCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UpdateProductCommand(
                Guid.NewGuid(), product.Id, "Widget", category.Id, unit.Id, null, true, 100m, 80m,
                VatRate.NoVat, 0, true, true),
            CancellationToken.None));
    }
}
