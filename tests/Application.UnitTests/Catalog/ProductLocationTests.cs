using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.UpdateProduct;
using ErpApp.Application.Catalog.Queries.GetProduct;
using ErpApp.Application.Catalog.Queries.ListProducts;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Catalog;

/// <summary>
/// Phase 36 -- a Product's billing-location restriction, and the rule that gives it meaning:
/// <b>an empty set is every location, and the document line picker asks for one location's
/// products.</b>
///
/// <para>Proved live on 2026-09-11 rather than assumed, because no screen in the reference product
/// displays the field: a product scoped to POS Retail alone vanished from the Invoice form's line
/// picker while the document header said HeadOffice and came back the instant the header was
/// switched, with one <c>products-minimized?…&amp;location_id=…</c> call per switch.</para>
/// </summary>
public class ProductLocationTests
{
    private sealed record Seed(Guid OrganizationId, Guid CategoryId, Guid UnitId, Guid HeadOfficeId, Guid BranchId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var category = ProductCategory.Create(organizationId, "General", null);
        var unit = UnitOfMeasurement.Create(organizationId, "Piece", "pc");
        var headOffice = BillingLocation.CreateHeadOffice(organizationId);
        var branch = BillingLocation.Create(organizationId, "POS", "POS Retail", null, null);

        db.ProductCategories.Add(category);
        db.UnitsOfMeasurement.Add(unit);
        db.BillingLocations.Add(headOffice);
        db.BillingLocations.Add(branch);
        await db.SaveChangesAsync();

        return new Seed(organizationId, category.Id, unit.Id, headOffice.Id, branch.Id);
    }

    private static async Task<Guid> CreateProductAsync(
        IAppDbContext db, Seed seed, string name, IReadOnlyList<Guid>? locationIds)
    {
        var result = await new CreateProductCommandHandler(db, new FakeDocumentNumberGenerator()).Handle(
            new CreateProductCommand(
                seed.OrganizationId, ProductType.Service, name, seed.CategoryId, seed.UnitId, null, true, 100m, 80m,
                VatRate.NoVat, 0, false, null, null, locationIds),
            CancellationToken.None);

        return result.Id;
    }

    private static Task<IReadOnlyList<string>> NamesAtAsync(IAppDbContext db, Seed seed, Guid? locationId) =>
        new ListProductsQueryHandler(db)
            .Handle(new ListProductsQuery(seed.OrganizationId, null, LocationId: locationId), CancellationToken.None)
            .ContinueWith(t => (IReadOnlyList<string>)t.Result.Items.Select(x => x.Name).OrderBy(x => x).ToList());

    [Fact]
    public async Task A_product_restricted_to_one_location_is_offered_only_there()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await CreateProductAsync(db, seed, "Branch Only", [seed.BranchId]);

        Assert.Equal(["Branch Only"], await NamesAtAsync(db, seed, seed.BranchId));

        // The live observation, in one assertion: the same product, the other location, no rows.
        Assert.Empty(await NamesAtAsync(db, seed, seed.HeadOfficeId));
    }

    [Fact]
    public async Task A_product_with_no_restriction_is_offered_everywhere()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await CreateProductAsync(db, seed, "Available Everywhere", null);
        await CreateProductAsync(db, seed, "Also Everywhere", []);

        Assert.Equal(["Also Everywhere", "Available Everywhere"], await NamesAtAsync(db, seed, seed.HeadOfficeId));
        Assert.Equal(["Also Everywhere", "Available Everywhere"], await NamesAtAsync(db, seed, seed.BranchId));
    }

    [Fact]
    public async Task The_products_grid_asks_for_no_location_and_sees_every_product()
    {
        // The restriction is not a hiding mechanism: the Products list has no location filter at
        // all in the reference product, and a restricted product must still be findable and
        // editable there.
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await CreateProductAsync(db, seed, "Branch Only", [seed.BranchId]);
        await CreateProductAsync(db, seed, "Everywhere", null);

        Assert.Equal(["Branch Only", "Everywhere"], await NamesAtAsync(db, seed, null));
    }

    [Fact]
    public async Task Updating_the_set_replaces_it_rather_than_accumulating()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var productId = await CreateProductAsync(db, seed, "Movable", [seed.BranchId]);

        await new UpdateProductCommandHandler(db).Handle(
            new UpdateProductCommand(
                seed.OrganizationId, productId, "Movable", seed.CategoryId, seed.UnitId, null, true, 100m, 80m,
                VatRate.NoVat, 0, false, true, null, null, null, null, null, null, [seed.HeadOfficeId]),
            CancellationToken.None);

        Assert.Equal(["Movable"], await NamesAtAsync(db, seed, seed.HeadOfficeId));
        Assert.Empty(await NamesAtAsync(db, seed, seed.BranchId));

        var rows = await db.ProductLocations.Where(x => x.ProductId == productId).ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task Clearing_the_set_makes_the_product_available_everywhere_again()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var productId = await CreateProductAsync(db, seed, "Freed", [seed.BranchId]);

        await new UpdateProductCommandHandler(db).Handle(
            new UpdateProductCommand(
                seed.OrganizationId, productId, "Freed", seed.CategoryId, seed.UnitId, null, true, 100m, 80m,
                VatRate.NoVat, 0, false, true, null, null, null, null, null, null, []),
            CancellationToken.None);

        // Empty means everywhere -- never nowhere.
        Assert.Equal(["Freed"], await NamesAtAsync(db, seed, seed.HeadOfficeId));
        Assert.Equal(["Freed"], await NamesAtAsync(db, seed, seed.BranchId));
        Assert.Empty(await db.ProductLocations.Where(x => x.ProductId == productId).ToListAsync());
    }

    [Fact]
    public async Task The_detail_read_returns_the_set_the_form_is_about_to_overwrite()
    {
        // Phase 35a's lesson in its own right: a write path with no matching read is a field a form
        // stores, never shows, and silently clears on the next save.
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var productId = await CreateProductAsync(db, seed, "Branch Only", [seed.BranchId]);

        var product = await new GetProductQueryHandler(db).Handle(
            new GetProductQuery(seed.OrganizationId, productId), CancellationToken.None);

        Assert.Equal(seed.BranchId, Assert.Single(product.Locations).LocationId);
    }
}
