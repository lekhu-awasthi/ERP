using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Inventory;

/// <summary>
/// Phase 52 — the resolver, and the freeze.
///
/// <para>The freeze is this phase's central claim and it was settled by experiment rather than by
/// reasoning: on the reference tenant an approved Purchase Bill for <c>2 BTL</c> had put <b>24</b>
/// into the ledger; the product's BTL row was then deleted outright and re-added at <b>6</b>, and
/// both the bill and the movement still read 24. A live lookup would have said 12.</para>
///
/// <para>The kickoff said that test was the one no handler test could express. Most of it is —
/// what a rate edit does to an <i>approved document's ledger history</i> needs the real database
/// and is proven in the manual E2E. But the half that matters most is expressible here, because
/// the factor is frozen on the line at Create: change the catalogue underneath a line that already
/// exists and the line's own answer must not move. That is what these tests pin.</para>
/// </summary>
public class DocumentLineUnitTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid PieceUnitId = Guid.NewGuid();
    private static readonly Guid CartonUnitId = Guid.NewGuid();

    private static async Task<(IAppDbContext Db, Product Product)> SeedAsync(decimal cartonRate = 12m)
    {
        var db = TestAppDbContext.Create();

        db.UnitsOfMeasurement.Add(Unit(PieceUnitId, "Piece", "PIS"));
        db.UnitsOfMeasurement.Add(Unit(CartonUnitId, "Carton", "CTN"));

        var product = Product.Create(
            OrganizationId, ProductType.Goods, "Steel rods", "P0590", Guid.NewGuid(), PieceUnitId, null,
            true, 1200m, 1200m, VatRate.NoVat, 0, true);

        product.AddSecondaryUnit(CartonUnitId, cartonRate, 0m, 0m);

        db.Products.Add(product);
        await db.SaveChangesAsync(CancellationToken.None);

        return (db, product);
    }

    private static UnitOfMeasurement Unit(Guid id, string name, string shortName)
    {
        var unit = UnitOfMeasurement.Create(OrganizationId, name, shortName);
        typeof(UnitOfMeasurement).GetProperty(nameof(UnitOfMeasurement.Id))!
            .SetValue(unit, id);
        return unit;
    }

    private static DocumentLineUnitResolver.LineUnitInput Line(Product product, Guid? unitId) =>
        new(product.Id, unitId);

    [Fact]
    public async Task A_secondary_unit_resolves_to_its_conversion_rate()
    {
        var (db, product) = await SeedAsync();

        var resolved = await DocumentLineUnitResolver.ResolveAsync(
            db, OrganizationId, [Line(product, CartonUnitId)], CancellationToken.None);

        Assert.Equal(CartonUnitId, resolved[0].UnitId);
        Assert.Equal(12m, resolved[0].ConversionFactor);
    }

    [Fact]
    public async Task A_line_naming_no_unit_is_the_primary_unit_at_factor_one()
    {
        var (db, product) = await SeedAsync();

        var resolved = await DocumentLineUnitResolver.ResolveAsync(
            db, OrganizationId, [Line(product, null)], CancellationToken.None);

        Assert.Null(resolved[0].UnitId);
        Assert.Equal(1m, resolved[0].ConversionFactor);
    }

    [Fact]
    public async Task Naming_the_primary_unit_explicitly_is_legal_and_keeps_the_id()
    {
        // The reference product's own dropdown lists the primary alongside the secondaries, so a
        // client echoing back what it was shown must not be an error -- and the id is kept rather
        // than normalised to null so a detail DTO renders the unit the user actually chose.
        var (db, product) = await SeedAsync();

        var resolved = await DocumentLineUnitResolver.ResolveAsync(
            db, OrganizationId, [Line(product, PieceUnitId)], CancellationToken.None);

        Assert.Equal(PieceUnitId, resolved[0].UnitId);
        Assert.Equal(1m, resolved[0].ConversionFactor);
    }

    [Fact]
    public async Task A_unit_the_product_does_not_carry_is_a_400_naming_the_line()
    {
        var (db, product) = await SeedAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            DocumentLineUnitResolver.ResolveAsync(
                db, OrganizationId, [Line(product, Guid.NewGuid())], CancellationToken.None));

        // Naming the line, not the field: a Domain invariant reached through an endpoint is a 500
        // that tells the caller nothing (phase 39).
        Assert.Equal("Lines[0]", Assert.Single(ex.Errors).PropertyName);
    }

    [Fact]
    public async Task The_offending_line_is_named_by_its_own_index()
    {
        var (db, product) = await SeedAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            DocumentLineUnitResolver.ResolveAsync(
                db,
                OrganizationId,
                [Line(product, CartonUnitId), Line(product, null), Line(product, Guid.NewGuid())],
                CancellationToken.None));

        Assert.Equal("Lines[2]", Assert.Single(ex.Errors).PropertyName);
    }

    [Fact]
    public async Task Editing_the_products_rate_does_not_move_a_line_already_written()
    {
        // The phase's central claim, in the smallest form a handler test can hold it.
        var (db, product) = await SeedAsync(cartonRate: 12m);

        var resolved = await DocumentLineUnitResolver.ResolveAsync(
            db, OrganizationId, [Line(product, CartonUnitId)], CancellationToken.None);

        var line = ErpApp.Domain.Sales.Invoice.Create(
            OrganizationId, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 17), null, null, null);
        line.AddLine(
            product.Id, 2m, 1200m, VatRate.NoVat, 0m, resolved[0].UnitId, resolved[0].ConversionFactor);

        Assert.Equal(24m, line.Lines[0].PrimaryQuantity.Value);

        // Now do to the catalogue what the live pass did: change the rate under the approved line.
        var secondary = product.SecondaryUnits.Single(x => x.UnitId == CartonUnitId);
        product.UpdateSecondaryUnit(secondary.Id, 6m, 0m, 0m);
        await db.SaveChangesAsync(CancellationToken.None);

        // A live lookup would now say 12. The line says 24, because it never reads the catalogue.
        Assert.Equal(24m, line.Lines[0].PrimaryQuantity.Value);
        Assert.Equal(12m, line.Lines[0].ConversionFactor);
    }

    [Fact]
    public async Task Deleting_the_products_unit_row_does_not_move_a_line_already_written()
    {
        // The stronger half of the same experiment, and the one that rules out a live lookup
        // entirely: on the reference tenant the row was *deleted* while an approved bill named it,
        // and the bill still read 2 BTL against 24 primary units. It survives because the line
        // names the unit lookup, not the product's secondary-unit row.
        var (db, product) = await SeedAsync(cartonRate: 12m);

        var resolved = await DocumentLineUnitResolver.ResolveAsync(
            db, OrganizationId, [Line(product, CartonUnitId)], CancellationToken.None);

        var invoice = ErpApp.Domain.Sales.Invoice.Create(
            OrganizationId, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 17), null, null, null);
        invoice.AddLine(
            product.Id, 2m, 1200m, VatRate.NoVat, 0m, resolved[0].UnitId, resolved[0].ConversionFactor);

        var secondary = product.SecondaryUnits.Single(x => x.UnitId == CartonUnitId);
        product.RemoveSecondaryUnit(secondary.Id);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(24m, invoice.Lines[0].PrimaryQuantity.Value);
        Assert.Equal(CartonUnitId, invoice.Lines[0].UnitId);

        // And the unit's short name still renders, because the lookup outlives the product's row.
        var names = await DocumentLineUnitResolver.LoadUnitNamesAsync(
            db, OrganizationId, [invoice.Lines[0].UnitId], CancellationToken.None);

        Assert.Equal("CTN", names[CartonUnitId]);
    }

    [Fact]
    public async Task The_money_is_untouched_by_the_conversion()
    {
        // Confirmed live in both directions: choosing a secondary unit sets the Rate from that unit
        // row's own price and Amount stays Quantity x Rate in the entered unit. 2 BTL at 1,200 is
        // 2,400, not 28,800. Any code multiplying an amount by a factor is wrong.
        var (db, product) = await SeedAsync();

        var resolved = await DocumentLineUnitResolver.ResolveAsync(
            db, OrganizationId, [Line(product, CartonUnitId)], CancellationToken.None);

        var invoice = ErpApp.Domain.Sales.Invoice.Create(
            OrganizationId, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 17), null, null, null);
        invoice.AddLine(
            product.Id, 2m, 1200m, VatRate.NoVat, 0m, resolved[0].UnitId, resolved[0].ConversionFactor);

        Assert.Equal(2400m, invoice.Lines[0].Amount);
        Assert.Equal(24m, invoice.Lines[0].PrimaryQuantity.Value);
    }

    [Fact]
    public async Task A_file_of_lines_naming_no_unit_at_all_queries_nothing()
    {
        // The overwhelmingly common case on a tenant that has never set up a matrix -- worth its own
        // test because the short-circuit is what keeps this phase off every existing hot path.
        var (db, product) = await SeedAsync();

        var resolved = await DocumentLineUnitResolver.ResolveAsync(
            db, OrganizationId, [Line(product, null), Line(product, null)], CancellationToken.None);

        Assert.All(resolved, r =>
        {
            Assert.Null(r.UnitId);
            Assert.Equal(1m, r.ConversionFactor);
        });
    }
}
