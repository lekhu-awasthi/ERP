using ErpApp.Application.Catalog.Commands.AddSecondaryUnit;
using ErpApp.Application.Catalog.Commands.DeleteSecondaryUnit;
using ErpApp.Application.Catalog.Commands.UpdateSecondaryUnit;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Catalog;

/// <summary>
/// Phase 45 -- multi-UOM x variants (the phase-24 carried item), settled by a live read rather than
/// in advance.
///
/// <para><b>The finding these tests pin.</b> On the reference tenant, variant
/// <c>18001 Iphone 16 Pro Max XXL Blue</c> carries primary unit <i>Number (NOS)</i> while its parent
/// and its three siblings carry <i>Piecess (ppp)</i>; each variant's own detail page has its own
/// Secondary Unit table with its own ADD NEW and a per-row Action column, and the Add dialog opens
/// blank rather than pre-filled from the parent. So a variant <b>owns</b> its unit matrix. A variant
/// <i>parent</i>, by contrast, has no Inventory Details panel at all -- no stock, no Secondary Unit
/// tab -- which is the same fact as phase 24's rule that a parent may not reach a document line.
/// </para>
///
/// <para>Ownership needed no schema change, because a variant <i>is</i> a Product and this
/// collection hangs off the row; the refusal, the edit and the delete are the work.</para>
/// </summary>
public class SecondaryUnitLifecycleTests
{
    [Fact]
    public async Task Two_variants_of_one_parent_hold_independent_unit_matrices()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedVariantMatrixAsync(db);

        await Add(db, seed.OrganizationId, seed.BlueVariantId, seed.BoxUnitId, 12m, 1200m, 900m);
        await Add(db, seed.OrganizationId, seed.RedVariantId, seed.BoxUnitId, 6m, 700m, 500m);

        var blue = await db.ProductSecondaryUnits.SingleAsync(x => x.ProductId == seed.BlueVariantId);
        var red = await db.ProductSecondaryUnits.SingleAsync(x => x.ProductId == seed.RedVariantId);

        // The same unit of measurement, two different conversion rates -- which is only possible if
        // the row hangs off the variant rather than off the matrix it belongs to.
        Assert.Equal(seed.BoxUnitId, blue.UnitId);
        Assert.Equal(seed.BoxUnitId, red.UnitId);
        Assert.Equal(12m, blue.ConversionRate);
        Assert.Equal(6m, red.ConversionRate);
    }

    [Fact]
    public async Task Editing_one_variants_conversion_leaves_its_sibling_untouched()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedVariantMatrixAsync(db);

        var blue = await Add(db, seed.OrganizationId, seed.BlueVariantId, seed.BoxUnitId, 12m, 1200m, 900m);
        await Add(db, seed.OrganizationId, seed.RedVariantId, seed.BoxUnitId, 12m, 1200m, 900m);

        await new UpdateSecondaryUnitCommandHandler(db).Handle(
            new UpdateSecondaryUnitCommand(seed.OrganizationId, seed.BlueVariantId, blue.Id, 24m, 2400m, 1800m),
            CancellationToken.None);

        var sibling = await db.ProductSecondaryUnits.SingleAsync(x => x.ProductId == seed.RedVariantId);
        Assert.Equal(12m, sibling.ConversionRate);
        Assert.Equal(1200m, sibling.SellingPrice);

        var edited = await db.ProductSecondaryUnits.SingleAsync(x => x.Id == blue.Id);
        Assert.Equal(24m, edited.ConversionRate);
        Assert.Equal(2400m, edited.SellingPrice);
        Assert.Equal(1800m, edited.PurchasePrice);
        Assert.Equal(seed.BoxUnitId, edited.UnitId);
    }

    /// <summary>
    /// A new variant starts with an empty matrix, which is what the live "New Variant Product" form
    /// says by carrying no unit field at all: Name, Code, one select per attribute, Selling Price,
    /// Purchase Price and nothing else.
    /// </summary>
    [Fact]
    public async Task A_new_variant_inherits_the_parents_primary_unit_but_none_of_its_secondary_units()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedVariantMatrixAsync(db);

        var parent = await db.Products.SingleAsync(x => x.Id == seed.ParentId);
        var blue = await db.Products.SingleAsync(x => x.Id == seed.BlueVariantId);

        Assert.Equal(parent.PrimaryUnitId, blue.PrimaryUnitId);
        Assert.Empty(await db.ProductSecondaryUnits.Where(x => x.ProductId == seed.BlueVariantId).ToListAsync());
    }

    [Fact]
    public async Task A_variant_parent_is_refused_a_secondary_unit_with_a_conflict_naming_the_reason()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedVariantMatrixAsync(db);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Add(db, seed.OrganizationId, seed.ParentId, seed.BoxUnitId, 12m, 1200m, 900m));

        // A ConflictException, not the Domain's InvalidOperationException: a Domain invariant reached
        // through an endpoint is a 500, which tells the caller nothing (phase-39).
        Assert.Contains("has variants", ex.Message, StringComparison.Ordinal);
        Assert.Empty(await db.ProductSecondaryUnits.ToListAsync());
    }

    [Fact]
    public async Task A_parent_is_refused_the_edit_as_well_as_the_add()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedVariantMatrixAsync(db);

        await Assert.ThrowsAsync<ConflictException>(() =>
            new UpdateSecondaryUnitCommandHandler(db).Handle(
                new UpdateSecondaryUnitCommand(seed.OrganizationId, seed.ParentId, Guid.NewGuid(), 2m, 1m, 1m),
                CancellationToken.None));
    }

    /// <summary>
    /// <b>The delete is the one verb a parent keeps, and this is the case that proves it must be.</b>
    /// A product can hold secondary units and be promoted to a variant parent <i>afterwards</i> --
    /// SetProductVariantAttributesCommand does not refuse that, because the pool is the thing being
    /// set. Its rows are then stale. Refusing the delete as well would make them permanent: hidden
    /// on the form and unremovable through the API.
    ///
    /// <para>Found by driving the browser pass, not by a test: every test above constructs the
    /// product in its final role, so none of them could reach this state.</para>
    /// </summary>
    [Fact]
    public async Task A_product_promoted_to_a_parent_can_still_delete_the_units_it_already_held()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedVariantMatrixAsync(db);

        // An ordinary product with a unit matrix...
        var plain = Product.Create(
            seed.OrganizationId, ProductType.Goods, "Plain Mug", "PRD-0009", seed.CategoryId,
            seed.PieceUnitId, null, true, 100m, 80m, VatRate.NoVat, 0, true);
        db.Products.Add(plain);
        await db.SaveChangesAsync(CancellationToken.None);

        var row = await Add(db, seed.OrganizationId, plain.Id, seed.BoxUnitId, 12m, 1200m, 900m);

        // ...promoted to a variant parent afterwards.
        var usages = plain.SetVariantAttributeUsages([(seed.ColourAttributeId, seed.BlueOptionId)]);
        db.ProductVariantAttributeUsages.AddRange(usages.Added);
        await db.SaveChangesAsync(CancellationToken.None);
        Assert.True((await db.Products.SingleAsync(x => x.Id == plain.Id)).HasVariants);

        // The add and the edit are refused...
        await Assert.ThrowsAsync<ConflictException>(() =>
            Add(db, seed.OrganizationId, plain.Id, seed.CartonUnitId, 48m, 4800m, 3840m));
        await Assert.ThrowsAsync<ConflictException>(() =>
            new UpdateSecondaryUnitCommandHandler(db).Handle(
                new UpdateSecondaryUnitCommand(seed.OrganizationId, plain.Id, row.Id, 24m, 1m, 1m),
                CancellationToken.None));

        // ...but the stale row can still be cleaned up.
        await new DeleteSecondaryUnitCommandHandler(db).Handle(
            new DeleteSecondaryUnitCommand(seed.OrganizationId, plain.Id, row.Id),
            CancellationToken.None);

        Assert.Empty(await db.ProductSecondaryUnits.Where(x => x.ProductId == plain.Id).ToListAsync());
    }

    [Fact]
    public async Task The_primary_unit_cannot_also_be_a_secondary_unit()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedVariantMatrixAsync(db);

        await Assert.ThrowsAsync<ConflictException>(() =>
            Add(db, seed.OrganizationId, seed.BlueVariantId, seed.PieceUnitId, 1m, 100m, 80m));
    }

    [Fact]
    public async Task One_unit_cannot_have_two_rows_on_the_same_product()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedVariantMatrixAsync(db);

        await Add(db, seed.OrganizationId, seed.BlueVariantId, seed.BoxUnitId, 12m, 1200m, 900m);

        // Without this the product would carry two rates for "Box" and nothing would say which one a
        // document means.
        await Assert.ThrowsAsync<ConflictException>(() =>
            Add(db, seed.OrganizationId, seed.BlueVariantId, seed.BoxUnitId, 6m, 700m, 500m));

        Assert.Single(await db.ProductSecondaryUnits.Where(x => x.ProductId == seed.BlueVariantId).ToListAsync());
    }

    [Fact]
    public async Task Delete_removes_the_row_and_leaves_the_products_others_alone()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedVariantMatrixAsync(db);

        var box = await Add(db, seed.OrganizationId, seed.BlueVariantId, seed.BoxUnitId, 12m, 1200m, 900m);
        var carton = await Add(db, seed.OrganizationId, seed.BlueVariantId, seed.CartonUnitId, 48m, 4800m, 3600m);

        await new DeleteSecondaryUnitCommandHandler(db).Handle(
            new DeleteSecondaryUnitCommand(seed.OrganizationId, seed.BlueVariantId, box.Id),
            CancellationToken.None);

        var remaining = await db.ProductSecondaryUnits.Where(x => x.ProductId == seed.BlueVariantId).ToListAsync();
        Assert.Equal(carton.Id, Assert.Single(remaining).Id);
    }

    /// <summary>
    /// The row id is checked against <i>this</i> product's rows, so one product cannot edit or
    /// delete another's -- the tenant filter alone would not catch it.
    /// </summary>
    [Fact]
    public async Task Another_products_row_is_not_found_rather_than_edited()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedVariantMatrixAsync(db);

        var blueRow = await Add(db, seed.OrganizationId, seed.BlueVariantId, seed.BoxUnitId, 12m, 1200m, 900m);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new UpdateSecondaryUnitCommandHandler(db).Handle(
                new UpdateSecondaryUnitCommand(seed.OrganizationId, seed.RedVariantId, blueRow.Id, 99m, 1m, 1m),
                CancellationToken.None));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new DeleteSecondaryUnitCommandHandler(db).Handle(
                new DeleteSecondaryUnitCommand(seed.OrganizationId, seed.RedVariantId, blueRow.Id),
                CancellationToken.None));

        Assert.Equal(12m, (await db.ProductSecondaryUnits.SingleAsync(x => x.Id == blueRow.Id)).ConversionRate);
    }

    [Fact]
    public async Task Another_organizations_product_is_not_found()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedVariantMatrixAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new UpdateSecondaryUnitCommandHandler(db).Handle(
                new UpdateSecondaryUnitCommand(Guid.NewGuid(), seed.BlueVariantId, Guid.NewGuid(), 2m, 1m, 1m),
                CancellationToken.None));
    }

    private static Task<AddSecondaryUnitResult> Add(
        IAppDbContext db, Guid organizationId, Guid productId, Guid unitId,
        decimal rate, decimal sellingPrice, decimal purchasePrice)
    {
        return new AddSecondaryUnitCommandHandler(db).Handle(
            new AddSecondaryUnitCommand(organizationId, productId, unitId, rate, sellingPrice, purchasePrice),
            CancellationToken.None);
    }

    private sealed record Seed(
        Guid OrganizationId,
        Guid ParentId,
        Guid BlueVariantId,
        Guid RedVariantId,
        Guid PieceUnitId,
        Guid BoxUnitId,
        Guid CartonUnitId,
        Guid CategoryId,
        Guid ColourAttributeId,
        Guid BlueOptionId);

    private static async Task<Seed> SeedVariantMatrixAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();

        var category = ProductCategory.Create(organizationId, "Apparel", null);
        var piece = UnitOfMeasurement.Create(organizationId, "Piece", "pc");
        var box = UnitOfMeasurement.Create(organizationId, "Box", "box");
        var carton = UnitOfMeasurement.Create(organizationId, "Carton", "ctn");
        db.ProductCategories.Add(category);
        db.UnitsOfMeasurement.AddRange(piece, box, carton);

        var colour = VariantAttribute.Create(organizationId, "Colour");
        var blue = colour.AddOption("Blue");
        var red = colour.AddOption("Red");
        db.VariantAttributes.Add(colour);
        db.VariantAttributeOptions.AddRange(blue, red);
        await db.SaveChangesAsync(CancellationToken.None);

        var parent = Product.Create(
            organizationId, ProductType.Goods, "T-Shirt", "PRD-0001", category.Id, piece.Id, null, true,
            1000m, 800m, VatRate.NoVat, 0, true);
        db.Products.Add(parent);
        await db.SaveChangesAsync(CancellationToken.None);

        var usages = parent.SetVariantAttributeUsages([(colour.Id, blue.Id), (colour.Id, red.Id)]);
        db.ProductVariantAttributeUsages.AddRange(usages.Added);
        await db.SaveChangesAsync(CancellationToken.None);

        var blueVariant = parent.CreateVariant(
            "PRD-0002", "T-Shirt Blue", [(colour.Id, blue.Id)], 1000m, 800m, null, null);
        var redVariant = parent.CreateVariant(
            "PRD-0003", "T-Shirt Red", [(colour.Id, red.Id)], 1000m, 800m, null, null);
        db.Products.AddRange(blueVariant, redVariant);
        db.ProductVariantValues.AddRange(blueVariant.VariantValues);
        db.ProductVariantValues.AddRange(redVariant.VariantValues);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(
            organizationId, parent.Id, blueVariant.Id, redVariant.Id, piece.Id, box.Id, carton.Id,
            category.Id, colour.Id, blue.Id);
    }
}
