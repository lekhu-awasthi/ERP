using ErpApp.Application.Catalog.Commands.AddVariantAttributeOption;
using ErpApp.Application.Catalog.Commands.CreateProductVariant;
using ErpApp.Application.Catalog.Commands.CreateVariantAttribute;
using ErpApp.Application.Catalog.Commands.DeleteProductVariant;
using ErpApp.Application.Catalog.Commands.SetProductVariantAttributes;
using ErpApp.Application.Catalog.Commands.UpdateProductVariant;
using ErpApp.Application.Catalog.Commands.UpdateVariantAttributeOption;
using ErpApp.Application.Catalog.Queries.ListProductVariants;
using ErpApp.Application.Catalog.Queries.ListProducts;
using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Catalog;

public class VariantAttributeCommandHandlerTests
{
    private sealed record Fixture(IAppDbContext Db, Guid OrganizationId, Guid ProductId, VariantAttribute Color);

    private static async Task<Fixture> SeedAsync()
    {
        var db = TestAppDbContext.Create();
        var orgId = Guid.NewGuid();

        var color = VariantAttribute.Create(orgId, "Color");
        color.AddOption("Red");
        color.AddOption("Blue");

        var product = Product.Create(
            orgId, ProductType.Goods, "T-Shirt", "P-0001", Guid.NewGuid(), Guid.NewGuid(), null,
            true, 500m, 300m, VatRate.ThirteenPercentVat, 0, true);

        db.VariantAttributes.Add(color);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return new Fixture(db, orgId, product.Id, color);
    }

    private static List<VariantCombinationInput> Pool(Fixture f) =>
        f.Color.Options.Select(o => new VariantCombinationInput(f.Color.Id, o.Id)).ToList();

    // ---- the catalog ----

    [Fact]
    public async Task Creating_an_attribute_stores_its_options_in_order()
    {
        var db = TestAppDbContext.Create();
        var orgId = Guid.NewGuid();

        var result = await new CreateVariantAttributeCommandHandler(db).Handle(
            new CreateVariantAttributeCommand(orgId, "Size", ["S", "M", "L"]), CancellationToken.None);

        Assert.Equal("Size", result.Name);
        Assert.Equal(["S", "M", "L"], result.Options.Select(x => x.Value));
    }

    [Fact]
    public async Task A_duplicate_option_in_the_create_form_is_a_409_not_a_500()
    {
        var db = TestAppDbContext.Create();

        await Assert.ThrowsAsync<ConflictException>(
            () => new CreateVariantAttributeCommandHandler(db).Handle(
                new CreateVariantAttributeCommand(Guid.NewGuid(), "Size", ["S", "s"]), CancellationToken.None));
    }

    [Fact]
    public async Task Adding_a_duplicate_option_later_is_also_a_409()
    {
        var f = await SeedAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => new AddVariantAttributeOptionCommandHandler(f.Db).Handle(
                new AddVariantAttributeOptionCommand(f.OrganizationId, f.Color.Id, "red"), CancellationToken.None));
    }

    [Fact]
    public async Task Retiring_a_catalog_option_is_allowed_even_when_variants_are_built_from_it()
    {
        // Decision C: deactivation is forward-looking. Existing variants keep working; only new
        // ones stop being offerable.
        var f = await SeedAsync();
        await SetPoolAsync(f);
        await AddVariantAsync(f, f.Color.Options[0].Id);

        var result = await new UpdateVariantAttributeOptionCommandHandler(f.Db).Handle(
            new UpdateVariantAttributeOptionCommand(
                f.OrganizationId, f.Color.Id, f.Color.Options[0].Id, "Red", IsActive: false),
            CancellationToken.None);

        Assert.False(result.Options.Single(x => x.Id == f.Color.Options[0].Id).IsActive);
        Assert.Equal(1, await f.Db.Products.CountAsync(x => x.ParentProductId == f.ProductId));
    }

    [Fact]
    public async Task An_attribute_from_another_organization_is_a_404()
    {
        var f = await SeedAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new AddVariantAttributeOptionCommandHandler(f.Db).Handle(
                new AddVariantAttributeOptionCommand(Guid.NewGuid(), f.Color.Id, "Green"), CancellationToken.None));
    }

    // ---- the product's own pool ----

    private static async Task SetPoolAsync(Fixture f) =>
        await new SetProductVariantAttributesCommandHandler(f.Db).Handle(
            new SetProductVariantAttributesCommand(f.OrganizationId, f.ProductId, Pool(f)), CancellationToken.None);

    private static async Task<ProductVariantResult> AddVariantAsync(Fixture f, Guid optionId) =>
        await new CreateProductVariantCommandHandler(f.Db, new FakeDocumentNumberGenerator()).Handle(
            new CreateProductVariantCommand(
                f.OrganizationId, f.ProductId, [new VariantCombinationInput(f.Color.Id, optionId)],
                null, null, null, 500m, 300m),
            CancellationToken.None);

    [Fact]
    public async Task Dropping_an_option_a_variant_is_built_from_is_refused()
    {
        // Decision C places the refusal here rather than on the catalog option itself.
        var f = await SeedAsync();
        await SetPoolAsync(f);
        await AddVariantAsync(f, f.Color.Options[0].Id);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => new SetProductVariantAttributesCommandHandler(f.Db).Handle(
                new SetProductVariantAttributesCommand(
                    f.OrganizationId, f.ProductId, [new VariantCombinationInput(f.Color.Id, f.Color.Options[1].Id)]),
                CancellationToken.None));

        Assert.Contains("built from it", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dropping_an_unused_option_is_allowed()
    {
        var f = await SeedAsync();
        await SetPoolAsync(f);
        await AddVariantAsync(f, f.Color.Options[0].Id);

        var result = await new SetProductVariantAttributesCommandHandler(f.Db).Handle(
            new SetProductVariantAttributesCommand(
                f.OrganizationId, f.ProductId, [new VariantCombinationInput(f.Color.Id, f.Color.Options[0].Id)]),
            CancellationToken.None);

        Assert.Single(result.Usages.Single().Options);
    }

    [Fact]
    public async Task Clearing_the_pool_while_variants_exist_is_refused()
    {
        var f = await SeedAsync();
        await SetPoolAsync(f);
        await AddVariantAsync(f, f.Color.Options[0].Id);

        await Assert.ThrowsAsync<ConflictException>(
            () => new SetProductVariantAttributesCommandHandler(f.Db).Handle(
                new SetProductVariantAttributesCommand(f.OrganizationId, f.ProductId, []), CancellationToken.None));
    }

    // ---- variants ----

    [Fact]
    public async Task Adding_the_same_combination_twice_is_a_409()
    {
        var f = await SeedAsync();
        await SetPoolAsync(f);
        await AddVariantAsync(f, f.Color.Options[0].Id);

        await Assert.ThrowsAsync<ConflictException>(() => AddVariantAsync(f, f.Color.Options[0].Id));
    }

    [Fact]
    public async Task A_variant_name_is_composed_from_the_parent_and_its_option_when_omitted()
    {
        var f = await SeedAsync();
        await SetPoolAsync(f);

        var variant = await AddVariantAsync(f, f.Color.Options[0].Id);

        Assert.Equal("T-Shirt Red", variant.Name);
        Assert.Equal("Color", variant.AttributeValues.Single().AttributeName);
        Assert.Equal("Red", variant.AttributeValues.Single().OptionValue);
    }

    [Fact]
    public async Task Updating_a_variant_changes_only_its_own_identity_fields()
    {
        var f = await SeedAsync();
        await SetPoolAsync(f);
        var variant = await AddVariantAsync(f, f.Color.Options[0].Id);

        var updated = await new UpdateProductVariantCommandHandler(f.Db).Handle(
            new UpdateProductVariantCommand(
                f.OrganizationId, variant.Id, "T-Shirt Crimson", "SKU-9", "BAR-9", 600m, 350m, IsActive: true),
            CancellationToken.None);

        Assert.Equal("T-Shirt Crimson", updated.Name);
        Assert.Equal("SKU-9", updated.Sku);
        Assert.Equal(600m, updated.SellingPrice);

        // The combination is untouched -- it is the variant's identity, not an editable field.
        Assert.Equal(variant.AttributeValues.Single().OptionId, updated.AttributeValues.Single().OptionId);
    }

    [Fact]
    public async Task Updating_a_non_variant_product_through_the_variant_command_is_refused()
    {
        var f = await SeedAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => new UpdateProductVariantCommandHandler(f.Db).Handle(
                new UpdateProductVariantCommand(f.OrganizationId, f.ProductId, "x", null, null, 1m, 1m, true),
                CancellationToken.None));
    }

    [Fact]
    public async Task Deleting_the_last_variant_demotes_the_parent_back_to_transactable()
    {
        var f = await SeedAsync();
        await SetPoolAsync(f);
        var variant = await AddVariantAsync(f, f.Color.Options[0].Id);

        await new DeleteProductVariantCommandHandler(f.Db).Handle(
            new DeleteProductVariantCommand(f.OrganizationId, variant.Id), CancellationToken.None);

        var parent = await f.Db.Products.SingleAsync(x => x.Id == f.ProductId);
        Assert.False(parent.HasVariants);
        Assert.Equal(0, await f.Db.Products.CountAsync(x => x.ParentProductId == f.ProductId));
    }

    [Fact]
    public async Task Deleting_a_variant_that_holds_stock_is_refused()
    {
        var f = await SeedAsync();
        await SetPoolAsync(f);
        var variant = await AddVariantAsync(f, f.Color.Options[0].Id);

        f.Db.StockLedgerEntries.Add(StockLedgerEntry.Create(
            f.OrganizationId, variant.Id, Guid.NewGuid(), 5m, 300m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 1)));
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => new DeleteProductVariantCommandHandler(f.Db).Handle(
                new DeleteProductVariantCommand(f.OrganizationId, variant.Id), CancellationToken.None));

        Assert.Contains("Deactivate it instead", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the panel and the picker's list ----

    [Fact]
    public async Task The_variant_panel_returns_the_pool_and_the_variants_together()
    {
        var f = await SeedAsync();
        await SetPoolAsync(f);
        await AddVariantAsync(f, f.Color.Options[0].Id);

        var panel = await new ListProductVariantsQueryHandler(f.Db).Handle(
            new ListProductVariantsQuery(f.OrganizationId, f.ProductId), CancellationToken.None);

        Assert.True(panel.HasVariants);
        Assert.Equal("Color", panel.AttributesUsed.Single().AttributeName);
        Assert.Equal(2, panel.AttributesUsed.Single().Options.Count);
        Assert.Single(panel.Variants);
    }

    [Fact]
    public async Task The_transactable_filter_hides_parents_and_the_parents_filter_shows_only_them()
    {
        var f = await SeedAsync();
        await SetPoolAsync(f);
        await AddVariantAsync(f, f.Color.Options[0].Id);
        var handler = new ListProductsQueryHandler(f.Db);

        var all = await handler.Handle(
            new ListProductsQuery(f.OrganizationId, null), CancellationToken.None);
        var transactable = await handler.Handle(
            new ListProductsQuery(f.OrganizationId, null, ProductVariantFilter.Transactable), CancellationToken.None);
        var parents = await handler.Handle(
            new ListProductsQuery(f.OrganizationId, null, ProductVariantFilter.VariantParents), CancellationToken.None);

        // Default is unchanged from before Phase 24 -- and matches the live product, which lists a
        // parent and its variants together.
        Assert.Equal(2, all.TotalCount);

        Assert.Single(transactable.Items);
        Assert.All(transactable.Items, p => Assert.False(p.HasVariants));

        Assert.Single(parents.Items);
        Assert.Equal(f.ProductId, parents.Items[0].Id);
    }
}
