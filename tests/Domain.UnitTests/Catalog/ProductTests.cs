using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.UnitTests.Catalog;

public class ProductTests
{
    [Fact]
    public void Create_starts_active_with_given_fields_and_fifo_valuation()
    {
        var organizationId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var unitId = Guid.NewGuid();

        var product = Product.Create(
            organizationId, ProductType.Goods, "Widget", "PRD-0001", categoryId, unitId,
            "1234.56", true, 150m, 100m, VatRate.ThirteenPercentVat, 10, true);

        Assert.Equal(organizationId, product.OrganizationId);
        Assert.Equal(ProductType.Goods, product.Type);
        Assert.Equal("Widget", product.Name);
        Assert.Equal("PRD-0001", product.Code);
        Assert.Equal(categoryId, product.CategoryId);
        Assert.Equal(unitId, product.PrimaryUnitId);
        Assert.Equal(ValuationMethod.Fifo, product.ValuationMethod);
        Assert.True(product.IsActive);
        Assert.Empty(product.SecondaryUnits);
    }

    [Fact]
    public void Update_replaces_editable_fields_but_not_type_or_code()
    {
        var product = Product.Create(
            Guid.NewGuid(), ProductType.Service, "Consulting", "PRD-0002", Guid.NewGuid(), Guid.NewGuid(),
            null, true, 0m, 0m, VatRate.NoVat, 0, false);
        var newCategoryId = Guid.NewGuid();
        var newUnitId = Guid.NewGuid();

        product.Update(
            "Consulting Services", newCategoryId, newUnitId, "9988.77", false, 500m, 0m,
            VatRate.ZeroVat, 5, true, false);

        Assert.Equal("Consulting Services", product.Name);
        Assert.Equal(newCategoryId, product.CategoryId);
        Assert.Equal(newUnitId, product.PrimaryUnitId);
        Assert.Equal(VatRate.ZeroVat, product.VatRate);
        Assert.False(product.IsActive);
        Assert.Equal(ProductType.Service, product.Type);
        Assert.Equal("PRD-0002", product.Code);
    }

    [Fact]
    public void AddSecondaryUnit_appends_to_the_collection()
    {
        var product = Product.Create(
            Guid.NewGuid(), ProductType.Goods, "Widget", "PRD-0003", Guid.NewGuid(), Guid.NewGuid(),
            null, true, 100m, 80m, VatRate.NoVat, 0, true);
        var secondaryUnitId = Guid.NewGuid();

        var secondaryUnit = product.AddSecondaryUnit(secondaryUnitId, 12m, 1000m, 900m);

        Assert.Single(product.SecondaryUnits);
        Assert.Equal(product.Id, secondaryUnit.ProductId);
        Assert.Equal(secondaryUnitId, secondaryUnit.UnitId);
        Assert.Equal(12m, secondaryUnit.ConversionRate);
    }
}
