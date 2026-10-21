using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.UpdatePosOrderDiscount;
using ErpApp.Application.Pos.Commands.UpdatePosOrderLineDiscount;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Queries.ListPosTillDiscounts;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Discounts;
using ErpApp.Application.Sales.DiscountSchemes;
using ErpApp.Application.UnitTests.CreditControl;
using ErpApp.Application.UnitTests.Pos;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Sales;

/// <summary>
/// Phase 97 -- discount schemes: what one reader (<see cref="DiscountSchemeRates"/>) says a product and a bill get, the
/// settings requests, and the three gates that keep a scheme's own percent without a discount key (an ERP draft, a Retail
/// till sale, a restaurant order). The user's rules (2026-10-21): percent only, the larger wins, a slab judged on the
/// bill's amount before VAT, and the scheme is where a line starts.
/// </summary>
public class DiscountSchemeHandlerTests
{
    private static readonly DateOnly Day = new(2026, 1, 10);

    private static Task<DiscountSchemeDto> CreateAsync(
        IAppDbContext db, Guid organizationId, DiscountSchemeKind kind, IReadOnlyList<DiscountSchemeLineDto> lines,
        string code = "DS1", DateOnly? start = null, DateOnly? end = null, IReadOnlyList<Guid>? locations = null) =>
        new CreateDiscountSchemeCommandHandler(db).Handle(
            new CreateDiscountSchemeCommand(
                organizationId, code, "Scheme " + code, kind, start ?? Day.AddDays(-5), end ?? Day.AddDays(5),
                locations is null, locations ?? [], lines),
            CancellationToken.None);

    private static DiscountSchemeLineDto Item(Guid productId, decimal pct) => new(productId, null, null, pct);

    private static DiscountSchemeLineDto Category(Guid categoryId, decimal pct) => new(null, categoryId, null, pct);

    private static DiscountSchemeLineDto Slab(decimal from, decimal pct) => new(null, null, from, pct);

    private static async Task<(IAppDbContext Db, CreditSeed Seed)> SeedAsync()
    {
        var db = TestAppDbContext.Create(Guid.NewGuid().ToString());
        var seed = await CreditLimitTestSeed.SeedAsync(db, sellingPrice: 150m, vatRate: VatRate.NoVat);
        return (db, seed);
    }

    // ---- the reader ----

    [Fact]
    public async Task A_product_gets_the_larger_of_its_own_scheme_and_its_categorys_or_any_ancestors()
    {
        var (db, seed) = await SeedAsync();
        var categoryId = await db.Products.Where(x => x.Id == seed.ProductId).Select(x => x.CategoryId).SingleAsync();
        var parent = ProductCategory.Create(seed.OrganizationId, "Everything", null);
        db.ProductCategories.Add(parent);
        (await db.ProductCategories.SingleAsync(x => x.Id == categoryId)).Update("General", parent.Id, true);
        await db.SaveChangesAsync();

        await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.ItemWise, [Item(seed.ProductId, 5m)], "A");
        await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.CategoryWise, [Category(parent.Id, 7.5m)], "B");

        var rates = await DiscountSchemeRates.LoadAsync(db, seed.OrganizationId, null, Day, [seed.ProductId], CancellationToken.None);

        Assert.Equal(7.5m, rates.ForProduct(seed.ProductId));
        Assert.Equal(0m, rates.ForProduct(Guid.NewGuid()));
    }

    /// <summary>A scheme on a product with variants covers each variant (Decision K). A variant's parent is set through
    /// the change tracker: the shape is the stored one, and making a real variant needs the attribute catalogue.</summary>
    [Fact]
    public async Task An_item_wise_row_on_a_parent_product_covers_its_variants()
    {
        var (db, seed) = await SeedAsync();
        var product = await db.Products.SingleAsync(x => x.Id == seed.ProductId);
        var parent = await new CreateProductCommandHandler(db, seed.NumberGenerator).Handle(
            new CreateProductCommand(
                seed.OrganizationId, ProductType.Service, "Consulting (all sizes)", product.CategoryId, product.PrimaryUnitId,
                null, true, 150m, 100m, VatRate.NoVat, 0, false),
            CancellationToken.None);
        ((Microsoft.EntityFrameworkCore.DbContext)db).Entry(product).Property(nameof(Product.ParentProductId)).CurrentValue = parent.Id;
        await db.SaveChangesAsync();

        await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.ItemWise, [Item(parent.Id, 4m)]);

        var rates = await DiscountSchemeRates.LoadAsync(db, seed.OrganizationId, null, Day, [seed.ProductId], CancellationToken.None);
        Assert.Equal(4m, rates.ForProduct(seed.ProductId));
    }

    [Fact]
    public async Task Only_an_active_scheme_whose_dates_cover_the_day_and_whose_locations_include_the_sale_applies()
    {
        var (db, seed) = await SeedAsync();
        var branch = BillingLocation.Create(seed.OrganizationId, "BR1", "Branch", null, seed.WarehouseId);
        var other = BillingLocation.Create(seed.OrganizationId, "BR2", "Other", null, seed.WarehouseId);
        db.BillingLocations.AddRange(branch, other);
        await db.SaveChangesAsync();

        await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.ItemWise, [Item(seed.ProductId, 3m)], "ALL");
        await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.ItemWise, [Item(seed.ProductId, 9m)], "BR", locations: [branch.Id]);
        var ended = await CreateAsync(
            db, seed.OrganizationId, DiscountSchemeKind.ItemWise, [Item(seed.ProductId, 20m)], "OLD",
            start: Day.AddDays(-30), end: Day.AddDays(-1));
        var off = await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.ItemWise, [Item(seed.ProductId, 30m)], "OFF");
        await new UpdateDiscountSchemeCommandHandler(db).Handle(
            new UpdateDiscountSchemeCommand(
                seed.OrganizationId, off.Id, off.Code, off.Name, off.Kind, off.StartDate, off.EndDate, true, [],
                off.Lines, IsActive: false),
            CancellationToken.None);

        async Task<decimal> At(Guid? location, DateOnly day) =>
            (await DiscountSchemeRates.LoadAsync(db, seed.OrganizationId, location, day, [seed.ProductId], CancellationToken.None))
            .ForProduct(seed.ProductId);

        Assert.Equal(9m, await At(branch.Id, Day));
        Assert.Equal(3m, await At(other.Id, Day));
        Assert.Equal(3m, await At(null, Day)); // a document with no location gets only the every-location schemes
        Assert.Equal(20m, await At(other.Id, ended.EndDate));
        Assert.Equal(0m, await At(other.Id, Day.AddDays(6)));
    }

    [Fact]
    public async Task Two_slab_schemes_merge_into_one_ladder_taking_the_larger_percent_at_every_amount()
    {
        var (db, seed) = await SeedAsync();
        await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.Slab, [Slab(1000m, 2m), Slab(5000m, 5m)], "S1");
        await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.Slab, [Slab(3000m, 3m), Slab(10000m, 4m)], "S2");

        var rates = await DiscountSchemeRates.LoadAsync(db, seed.OrganizationId, null, Day, [], CancellationToken.None);

        Assert.Equal(
            [new SlabStep(1000m, 2m), new SlabStep(3000m, 3m), new SlabStep(5000m, 5m)],
            rates.Slabs);
        Assert.Equal(0m, rates.ForBillAmount(999.99m));
        Assert.Equal(2m, rates.ForBillAmount(1000m));
        Assert.Equal(3m, rates.ForBillAmount(4999.99m));
        Assert.Equal(5m, rates.ForBillAmount(50000m));
    }

    [Fact]
    public async Task The_forms_query_answers_what_the_reader_does()
    {
        var (db, seed) = await SeedAsync();
        await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.ItemWise, [Item(seed.ProductId, 4m)], "A");
        await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.Slab, [Slab(500m, 1.5m)], "B");

        var answer = await new ListApplicableDiscountsQueryHandler(db).Handle(
            new ListApplicableDiscountsQuery(seed.OrganizationId, null, Day, [seed.ProductId]), CancellationToken.None);

        Assert.Equal(new ProductDiscountDto(seed.ProductId, 4m), Assert.Single(answer.Products));
        Assert.Equal(new SlabStep(500m, 1.5m), Assert.Single(answer.Slabs));
    }

    // ---- the settings requests ----

    [Fact]
    public async Task A_code_is_the_organizations_own_and_every_product_named_must_be_its_own()
    {
        var (db, seed) = await SeedAsync();
        await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.ItemWise, [Item(seed.ProductId, 5m)]);

        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.ItemWise, [Item(seed.ProductId, 5m)]));
        var unknown = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.ItemWise, [Item(Guid.NewGuid(), 5m)], "DS2"));
        Assert.Contains(unknown.Errors, e => e.PropertyName == "Lines");
    }

    [Fact]
    public async Task An_edit_replaces_the_rows_and_a_delete_removes_them()
    {
        var (db, seed) = await SeedAsync();
        var created = await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.Slab, [Slab(1000m, 2m), Slab(5000m, 5m)]);

        var updated = await new UpdateDiscountSchemeCommandHandler(db).Handle(
            new UpdateDiscountSchemeCommand(
                seed.OrganizationId, created.Id, "DS1", "Festival", DiscountSchemeKind.ItemWise, Day, Day, true, [],
                [Item(seed.ProductId, 6m)], IsActive: true),
            CancellationToken.None);

        Assert.Equal((DiscountSchemeKind.ItemWise, "Festival"), (updated.Kind, updated.Name));
        Assert.Equal([Item(seed.ProductId, 6m)], updated.Lines);
        Assert.Equal(1, await db.DiscountSchemeLines.CountAsync());

        await new DeleteDiscountSchemeCommandHandler(db).Handle(
            new DeleteDiscountSchemeCommand(seed.OrganizationId, created.Id), CancellationToken.None);
        Assert.Empty(await db.DiscountSchemes.ToListAsync());
        Assert.Empty(await db.DiscountSchemeLines.ToListAsync());
    }

    [Fact]
    public void The_validators_name_each_broken_rule()
    {
        var validator = new CreateDiscountSchemeCommandValidator();

        var result = validator.Validate(new CreateDiscountSchemeCommand(
            Guid.NewGuid(), "", "Name", DiscountSchemeKind.Slab, Day, Day.AddDays(-1), false, [],
            [Slab(100m, 0m), Slab(100m, 5m), new DiscountSchemeLineDto(Guid.NewGuid(), null, null, 5m)]));

        Assert.Contains(result.Errors, e => e.PropertyName == "Code");
        Assert.Contains(result.Errors, e => e.PropertyName == "EndDate");
        Assert.Contains(result.Errors, e => e.PropertyName == "LocationIds");
        Assert.Contains(result.Errors, e => e.PropertyName == "Lines[0].DiscountPct");
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Each row of a slab scheme names the bill amount it starts at.");
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Two slabs start at the same bill amount.");

        Assert.False(new UpdateDiscountSchemeCommandValidator().Validate(new UpdateDiscountSchemeCommand(
            Guid.NewGuid(), Guid.Empty, "X", "Y", (DiscountSchemeKind)(-1), Day, Day, true, [], [Slab(0m, 1m)], true)).IsValid);
        Assert.True(new ListApplicableDiscountsQueryValidator().Validate(
            new ListApplicableDiscountsQuery(Guid.NewGuid(), null, Day, [])).IsValid);
        Assert.True(new ListPosTillDiscountsQueryValidator().Validate(
            new ListPosTillDiscountsQuery(Guid.NewGuid(), Guid.NewGuid(), [])).IsValid);
    }

    // ---- the ERP's price gate (phase 81) ----

    private static Task<CreateInvoiceResult> InvoiceAsync(
        IAppDbContext db, CreditSeed seed, IReadOnlyList<InvoiceLineInput> lines, decimal discountPct = 0m,
        string? currencyCode = null, decimal? exchangeRate = null) =>
        new CreateInvoiceCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, Day, null, lines, null, null, discountPct)
            {
                CurrencyCode = currencyCode,
                ExchangeRate = exchangeRate,
            },
            CancellationToken.None);

    [Fact]
    public async Task A_clerk_without_the_discount_key_may_keep_a_schemes_percent_but_not_change_it()
    {
        var (db, seed) = await SeedAsync();
        await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.ItemWise, [Item(seed.ProductId, 5m)], "A");
        await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.Slab, [Slab(1000m, 2m)], "B");

        // 10 x 150 less 5% is 1,425, which reaches the 1,000 slab.
        await InvoiceAsync(db, seed, [new InvoiceLineInput(seed.ProductId, 10m, 150m, VatRate.NoVat, 5m)], discountPct: 2m);

        var line = await Assert.ThrowsAsync<ForbiddenException>(() =>
            InvoiceAsync(db, seed, [new InvoiceLineInput(seed.ProductId, 10m, 150m, VatRate.NoVat, 6m)]));
        // 5 x 150 less 5% is 712.50, below the slab: its 2% is no longer the scheme's.
        var document = await Assert.ThrowsAsync<ForbiddenException>(() =>
            InvoiceAsync(db, seed, [new InvoiceLineInput(seed.ProductId, 5m, 150m, VatRate.NoVat, 5m)], discountPct: 2m));

        Assert.Contains("(Sales.Invoice.Discount)", line.Message);
        Assert.Contains("(Sales.Invoice.Discount)", document.Message);
        Assert.Single(await db.Invoices.ToListAsync());
    }

    /// <summary>A slab is in rupees, so a dollar invoice reaches it by its value in rupees.</summary>
    [Fact]
    public async Task A_foreign_currency_document_reaches_a_slab_by_its_value_in_the_base_currency()
    {
        var (db, seed) = await SeedAsync();
        await CreateAsync(db, seed.OrganizationId, DiscountSchemeKind.Slab, [Slab(1000m, 2m)]);

        // 8 x 1 USD at 133 is 1,064 rupees.
        await InvoiceAsync(
            db, seed, [new InvoiceLineInput(seed.ProductId, 8m, 1m, VatRate.NoVat)], discountPct: 2m, currencyCode: "USD",
            exchangeRate: 133m);
        await Assert.ThrowsAsync<ForbiddenException>(() => InvoiceAsync(
            db, seed, [new InvoiceLineInput(seed.ProductId, 7m, 1m, VatRate.NoVat)], discountPct: 2m, currencyCode: "USD",
            exchangeRate: 133m));
    }

    // ---- the Retail till ----

    private static readonly string[] CashierWithoutDiscount =
        [.. PosTestTill.CashierKeys.Where(x => x != PermissionKeys.InvoiceTillDiscount)];

    [Fact]
    public async Task A_cashier_without_the_discount_key_sells_at_the_schemes_percent_and_slab_and_no_other()
    {
        var till = await PosTestTill.CreateAsync(CashierWithoutDiscount);
        await CreateAsync(till.Db, till.OrganizationId, DiscountSchemeKind.ItemWise, [Item(till.MomoId, 10m)], "A",
            start: PosTestTill.Today, end: PosTestTill.Today);
        await CreateAsync(till.Db, till.OrganizationId, DiscountSchemeKind.Slab, [Slab(300m, 5m)], "B",
            start: PosTestTill.Today, end: PosTestTill.Today);
        var session = await till.OpenAsync();

        var offered = await new ListPosTillDiscountsQueryHandler(till.Db).Handle(
            new ListPosTillDiscountsQuery(till.OrganizationId, till.Location.Id, [till.MomoId, till.CokeId]), CancellationToken.None);
        Assert.Equal(new ProductDiscountDto(till.MomoId, 10m), Assert.Single(offered.Products));

        // 2 momo at 200 less 10% is 360, which reaches 300: 5% off the bill is 342.00, its 10% service charge 34.20, and
        // 13% VAT on both 48.91: 425.11, rounded 425.
        var sale = await till.SaleHandler().Handle(
            new CreatePosSaleCommand(
                till.OrganizationId, session.Id, till.Location.Id, [till.Momo(2) with { DiscountPct = 10m }],
                [till.Cash(425m)], 0m, null, till.Seed.WarehouseId) { DiscountPct = 5m },
            CancellationToken.None);
        Assert.Equal(425m, sale.GrandTotal);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => till.SellAsync(
            session.Id, [till.Coke(1) with { DiscountPct = 10m }], [till.Cash(61.02m)]));
        Assert.Contains(PermissionKeys.InvoiceTillDiscount, refused.Message, StringComparison.Ordinal);
    }

    // ---- a restaurant order ----

    [Fact]
    public async Task A_dish_starts_at_its_schemes_percent_and_the_order_follows_its_slab_until_set_by_hand()
    {
        var r = await PosTestRestaurant.CreateAsync(
            [.. PosTestRestaurant.WaiterKeys.Where(x => x != PermissionKeys.InvoiceTillDiscount)]);
        await CreateAsync(r.Till.Db, r.OrganizationId, DiscountSchemeKind.ItemWise, [Item(r.Till.MomoId, 10m)], "A",
            start: PosTestTill.Today, end: PosTestTill.Today);
        await CreateAsync(r.Till.Db, r.OrganizationId, DiscountSchemeKind.Slab, [Slab(500m, 5m), Slab(1000m, 8m)], "B",
            start: PosTestTill.Today, end: PosTestTill.Today);

        // 2 momo less 10% is 360 and 2 coke 120: 480, below the first slab.
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        Assert.Equal(10m, order.Lines.Single(x => x.ProductName == "Chicken Momo").DiscountPct);
        Assert.Equal(0m, order.Lines.Single(x => x.ProductName == "Coke 250ml").DiscountPct);
        Assert.Equal(0m, order.DiscountPct);

        // Two more momo: 840 reaches 500.
        order = await r.AddAsync(order.Id, [r.Momo(2)]);
        Assert.Equal(5m, order.DiscountPct);

        // Without the discount key a waiter may set the slab's own figure, and nothing else.
        await new UpdatePosOrderDiscountCommandHandler(r.Till.Db, r.CurrentUser()).Handle(
            new UpdatePosOrderDiscountCommand(r.OrganizationId, order.Id, 5m), CancellationToken.None);
        await Assert.ThrowsAsync<ForbiddenException>(() => new UpdatePosOrderDiscountCommandHandler(r.Till.Db, r.CurrentUser())
            .Handle(new UpdatePosOrderDiscountCommand(r.OrganizationId, order.Id, 6m), CancellationToken.None));
        var momo = order.Lines.First(x => x.ProductName == "Chicken Momo");
        await new UpdatePosOrderLineDiscountCommandHandler(r.Till.Db, r.CurrentUser()).Handle(
            new UpdatePosOrderLineDiscountCommand(r.OrganizationId, order.Id, momo.Id, 10m), CancellationToken.None);

        // Set by hand now (to the slab's own figure, by a waiter who may), so more food no longer moves it.
        order = await r.AddAsync(order.Id, [r.Coke(5)]);
        Assert.Equal(5m, order.DiscountPct);
    }
}
