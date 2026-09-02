using ErpApp.Application.Catalog.Commands.GenerateProductVariants;
using ErpApp.Application.Catalog.Commands.SetProductVariantAttributes;
using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Catalog;

/// <summary>
/// The roadmap's other exit criterion: "a two-attribute product generates its variant matrix".
/// Note the live reference product has no generator at all (it adds variants one at a time) -- see
/// GenerateProductVariantsCommand's doc comment for why this exists anyway.
/// </summary>
public class GenerateProductVariantsCommandHandlerTests
{
    private sealed record Fixture(
        IAppDbContext Db, Guid OrganizationId, Guid ProductId,
        VariantAttribute Color, VariantAttribute Size);

    private static async Task<Fixture> SeedAsync(int colors = 2, int sizes = 3)
    {
        var db = TestAppDbContext.Create();
        var orgId = Guid.NewGuid();

        var color = VariantAttribute.Create(orgId, "Color");
        foreach (var value in new[] { "Red", "Blue", "Green", "Black", "White" }.Take(colors))
        {
            color.AddOption(value);
        }

        var size = VariantAttribute.Create(orgId, "Size");
        foreach (var value in new[] { "S", "M", "L", "XL", "XXL" }.Take(sizes))
        {
            size.AddOption(value);
        }

        var product = Product.Create(
            orgId, ProductType.Goods, "T-Shirt", "P-0001", Guid.NewGuid(), Guid.NewGuid(), null,
            true, 500m, 300m, VatRate.ThirteenPercentVat, 0, true);

        db.VariantAttributes.AddRange(color, size);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return new Fixture(db, orgId, product.Id, color, size);
    }

    private static List<VariantCombinationInput> WholePool(Fixture f) =>
    [
        .. f.Color.Options.Select(o => new VariantCombinationInput(f.Color.Id, o.Id)),
        .. f.Size.Options.Select(o => new VariantCombinationInput(f.Size.Id, o.Id)),
    ];

    private static async Task SetPoolAsync(Fixture f)
    {
        await new SetProductVariantAttributesCommandHandler(f.Db).Handle(
            new SetProductVariantAttributesCommand(f.OrganizationId, f.ProductId, WholePool(f)),
            CancellationToken.None);
    }

    [Fact]
    public async Task A_two_attribute_product_generates_its_whole_matrix()
    {
        var f = await SeedAsync(colors: 2, sizes: 3);
        await SetPoolAsync(f);

        var result = await new GenerateProductVariantsCommandHandler(f.Db, new FakeDocumentNumberGenerator())
            .Handle(new GenerateProductVariantsCommand(f.OrganizationId, f.ProductId), CancellationToken.None);

        Assert.Equal(6, result.Created.Count);
        Assert.Equal(0, result.SkippedExisting);

        var variants = await f.Db.Products.Where(x => x.ParentProductId == f.ProductId).ToListAsync();
        Assert.Equal(6, variants.Count);

        // Every combination is distinct, and each takes exactly one value per attribute.
        Assert.Equal(6, variants.Select(x => x.CombinationKey).Distinct().Count());
        Assert.All(result.Created, v => Assert.Equal(2, v.AttributeValues.Count));
    }

    [Fact]
    public async Task Generation_promotes_the_parent_so_it_stops_being_transactable()
    {
        var f = await SeedAsync(colors: 2, sizes: 1);
        await SetPoolAsync(f);

        await new GenerateProductVariantsCommandHandler(f.Db, new FakeDocumentNumberGenerator())
            .Handle(new GenerateProductVariantsCommand(f.OrganizationId, f.ProductId), CancellationToken.None);

        var parent = await f.Db.Products.SingleAsync(x => x.Id == f.ProductId);
        Assert.True(parent.HasVariants);
    }

    [Fact]
    public async Task Re_running_generation_skips_what_already_exists_rather_than_duplicating()
    {
        // The property that makes "add a fifth colour and generate again" safe.
        var f = await SeedAsync(colors: 2, sizes: 2);
        await SetPoolAsync(f);
        var handler = new GenerateProductVariantsCommandHandler(f.Db, new FakeDocumentNumberGenerator());

        await handler.Handle(new GenerateProductVariantsCommand(f.OrganizationId, f.ProductId), CancellationToken.None);
        var second = await handler.Handle(
            new GenerateProductVariantsCommand(f.OrganizationId, f.ProductId), CancellationToken.None);

        Assert.Empty(second.Created);
        Assert.Equal(4, second.SkippedExisting);
        Assert.Equal(4, await f.Db.Products.CountAsync(x => x.ParentProductId == f.ProductId));
    }

    [Fact]
    public async Task Widening_the_pool_then_regenerating_fills_only_the_new_combinations()
    {
        var f = await SeedAsync(colors: 3, sizes: 2);
        var handler = new GenerateProductVariantsCommandHandler(f.Db, new FakeDocumentNumberGenerator());

        // Start with 2 colours x 2 sizes = 4.
        var narrow = new List<VariantCombinationInput>
        {
            new(f.Color.Id, f.Color.Options[0].Id),
            new(f.Color.Id, f.Color.Options[1].Id),
            new(f.Size.Id, f.Size.Options[0].Id),
            new(f.Size.Id, f.Size.Options[1].Id),
        };

        await new SetProductVariantAttributesCommandHandler(f.Db).Handle(
            new SetProductVariantAttributesCommand(f.OrganizationId, f.ProductId, narrow), CancellationToken.None);
        await handler.Handle(new GenerateProductVariantsCommand(f.OrganizationId, f.ProductId), CancellationToken.None);

        // Widen to 3 colours x 2 sizes = 6, and regenerate.
        await new SetProductVariantAttributesCommandHandler(f.Db).Handle(
            new SetProductVariantAttributesCommand(f.OrganizationId, f.ProductId, WholePool(f)), CancellationToken.None);
        var second = await handler.Handle(
            new GenerateProductVariantsCommand(f.OrganizationId, f.ProductId), CancellationToken.None);

        Assert.Equal(2, second.Created.Count);
        Assert.Equal(4, second.SkippedExisting);
        Assert.Equal(6, await f.Db.Products.CountAsync(x => x.ParentProductId == f.ProductId));
    }

    [Fact]
    public async Task Generated_variants_inherit_the_parents_prices_and_compose_their_names()
    {
        var f = await SeedAsync(colors: 1, sizes: 1);
        await SetPoolAsync(f);

        var result = await new GenerateProductVariantsCommandHandler(f.Db, new FakeDocumentNumberGenerator())
            .Handle(new GenerateProductVariantsCommand(f.OrganizationId, f.ProductId), CancellationToken.None);

        var variant = Assert.Single(result.Created);
        Assert.Equal(500m, variant.SellingPrice);
        Assert.Equal(300m, variant.PurchasePrice);
        Assert.StartsWith("T-Shirt", variant.Name, StringComparison.Ordinal);
        Assert.Contains("Red", variant.Name, StringComparison.Ordinal);
        Assert.Contains("S", variant.Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_selection_larger_than_the_cap_is_refused_rather_than_truncated()
    {
        // Decision C: a silent partial matrix is the worst outcome available, because the user
        // cannot tell which combinations are missing. 5 x 5 x 5 x 5 = 625 > 200.
        var db = TestAppDbContext.Create();
        var orgId = Guid.NewGuid();

        var attributes = new List<VariantAttribute>();
        for (var i = 0; i < 4; i++)
        {
            var attribute = VariantAttribute.Create(orgId, $"A{i}");
            for (var j = 0; j < 5; j++)
            {
                attribute.AddOption($"v{j}");
            }

            attributes.Add(attribute);
        }

        var product = Product.Create(
            orgId, ProductType.Goods, "Thing", "P-0001", Guid.NewGuid(), Guid.NewGuid(), null,
            true, 1m, 1m, VatRate.NoVat, 0, true);

        db.VariantAttributes.AddRange(attributes);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var pool = attributes
            .SelectMany(a => a.Options.Select(o => new VariantCombinationInput(a.Id, o.Id)))
            .ToList();

        await new SetProductVariantAttributesCommandHandler(db).Handle(
            new SetProductVariantAttributesCommand(orgId, product.Id, pool), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => new GenerateProductVariantsCommandHandler(db, new FakeDocumentNumberGenerator())
                .Handle(new GenerateProductVariantsCommand(orgId, product.Id), CancellationToken.None));

        Assert.Contains("625", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, await db.Products.CountAsync(x => x.ParentProductId == product.Id));
    }

    [Fact]
    public async Task Generating_with_no_attribute_options_selected_is_refused()
    {
        var f = await SeedAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => new GenerateProductVariantsCommandHandler(f.Db, new FakeDocumentNumberGenerator())
                .Handle(new GenerateProductVariantsCommand(f.OrganizationId, f.ProductId), CancellationToken.None));
    }

    [Fact]
    public async Task An_option_paired_with_the_wrong_attribute_is_rejected()
    {
        // Pairing Color's id with a Size option would otherwise produce a variant whose combination
        // reads as nonsense but whose CombinationKey is perfectly well-formed.
        var f = await SeedAsync();
        await SetPoolAsync(f);

        await Assert.ThrowsAsync<ConflictException>(
            () => new GenerateProductVariantsCommandHandler(f.Db, new FakeDocumentNumberGenerator())
                .Handle(
                    new GenerateProductVariantsCommand(
                        f.OrganizationId, f.ProductId, [new VariantCombinationInput(f.Color.Id, f.Size.Options[0].Id)]),
                    CancellationToken.None));
    }

    [Fact]
    public async Task Generating_on_a_product_from_another_organization_is_a_404()
    {
        var f = await SeedAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new GenerateProductVariantsCommandHandler(f.Db, new FakeDocumentNumberGenerator())
                .Handle(new GenerateProductVariantsCommand(Guid.NewGuid(), f.ProductId), CancellationToken.None));
    }
}
