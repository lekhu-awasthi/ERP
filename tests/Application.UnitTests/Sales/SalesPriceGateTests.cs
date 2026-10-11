using ErpApp.Application.Catalog.Commands.AddSecondaryUnit;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Catalog.Queries.SuggestProductRate;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.ApproveQuotation;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Commands.CreateQuotation;
using ErpApp.Application.Sales.Commands.CreateSalesOrder;
using ErpApp.Application.Sales.Commands.UpdateInvoice;
using ErpApp.Application.Sales.Commands.UpdateQuotation;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Queries.GetSalesPricePowers;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.UnitTests.CreditControl;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Sales;

/// <summary>
/// Phase 81 -- who may price an ERP sale (<see cref="SalesPriceGate"/>). The seed's one product,
/// <i>Consulting</i>, sells at 150 and carries no VAT, so 150 is its catalogue rate. The clerk is a
/// user with no grants at all, which is what a custom role with both keys revoked looks like to the
/// gate; the pricer holds both (<see cref="SalesPricing"/>).
/// </summary>
public class SalesPriceGateTests
{
    private static readonly DateOnly Day = new(2026, 1, 10);

    [Fact]
    public async Task A_clerk_may_raise_an_invoice_at_the_catalogue_price_without_either_key()
    {
        var (db, seed) = await SeedAsync();

        var created = await CreateInvoiceAsync(db, seed, Clerk(), [Line(seed, 150m)]);

        Assert.Equal(1, await db.InvoiceLines.CountAsync(x => x.InvoiceId == created.Id));
    }

    [Fact]
    public async Task A_line_discount_or_a_document_discount_needs_the_discount_key()
    {
        var (db, seed) = await SeedAsync();

        var line = await Assert.ThrowsAsync<ForbiddenException>(
            () => CreateInvoiceAsync(db, seed, Clerk(), [Line(seed, 150m, discountPct: 10m)]));
        var document = await Assert.ThrowsAsync<ForbiddenException>(
            () => CreateInvoiceAsync(db, seed, Clerk(), [Line(seed, 150m)], discountPct: 5m));

        Assert.Contains("(Sales.Invoice.Discount)", line.Message);
        Assert.Contains("(Sales.Invoice.Discount)", document.Message);
        Assert.Empty(db.Invoices);
    }

    /// <summary>Both directions, as at the till (phase 74 Decision C).</summary>
    [Theory]
    [InlineData(149)]
    [InlineData(151)]
    public async Task Any_rate_but_the_catalogues_needs_the_change_rate_key(decimal rate)
    {
        var (db, seed) = await SeedAsync();

        var refused = await Assert.ThrowsAsync<ForbiddenException>(
            () => CreateInvoiceAsync(db, seed, Clerk(), [Line(seed, rate)]));

        Assert.Contains("(Sales.Invoice.ChangeRate)", refused.Message);
    }

    [Fact]
    public async Task The_discount_is_asked_about_before_the_rate()
    {
        var (db, seed) = await SeedAsync();

        var refused = await Assert.ThrowsAsync<ForbiddenException>(
            () => CreateInvoiceAsync(db, seed, Clerk(), [Line(seed, 120m, discountPct: 10m)]));

        Assert.Contains("(Sales.Invoice.Discount)", refused.Message);
    }

    [Fact]
    public async Task Each_key_grants_only_its_own_power()
    {
        var (db, seed) = await SeedAsync();
        var discounter = await GrantedAsync(db, seed, PermissionKeys.InvoiceDiscount);

        await CreateInvoiceAsync(db, seed, discounter, [Line(seed, 150m, discountPct: 10m)], discountPct: 5m);
        var refused = await Assert.ThrowsAsync<ForbiddenException>(
            () => CreateInvoiceAsync(db, seed, discounter, [Line(seed, 140m)]));

        Assert.Contains("(Sales.Invoice.ChangeRate)", refused.Message);
    }

    /// <summary>Nothing to keep: a product sold at no catalogue price takes whatever is typed.</summary>
    [Fact]
    public async Task A_product_with_no_selling_price_takes_any_rate()
    {
        var (db, seed) = await SeedAsync(sellingPrice: 0m);

        await CreateInvoiceAsync(db, seed, Clerk(), [Line(seed, 275m)]);
    }

    /// <summary>The catalogue is in rupees, so a dollar rate has nothing to be compared with -- but a
    /// discount is a discount in any currency.</summary>
    [Fact]
    public async Task A_foreign_currency_document_is_judged_on_its_discount_alone()
    {
        var (db, seed) = await SeedAsync();

        await CreateInvoiceAsync(db, seed, Clerk(), [Line(seed, 1.25m)], currencyCode: "USD", exchangeRate: 133m);
        var refused = await Assert.ThrowsAsync<ForbiddenException>(
            () => CreateInvoiceAsync(
                db, seed, Clerk(), [Line(seed, 1.25m, discountPct: 10m)], currencyCode: "USD", exchangeRate: 133m));

        Assert.Contains("(Sales.Invoice.Discount)", refused.Message);
    }

    /// <summary>In Recent mode the form suggests the last sale's rate, so the clerk's unchanged
    /// suggestion is never the thing refused.</summary>
    [Fact]
    public async Task In_recent_mode_the_last_sales_rate_is_kept_as_well_as_the_catalogues()
    {
        var (db, seed) = await SeedAsync(suggestMode: SuggestSellingPriceMode.RecentSellingPrice);
        var pricer = await SalesPricing.PricerAsync(db, seed.OrganizationId);
        var sold = await CreateInvoiceAsync(db, seed, pricer, [Line(seed, 180m)]);
        await ApproveInvoiceAsync(db, seed, sold.Id);

        await CreateInvoiceAsync(db, seed, Clerk(), [Line(seed, 180m)]);
        await CreateInvoiceAsync(db, seed, Clerk(), [Line(seed, 150m)]);
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateInvoiceAsync(db, seed, Clerk(), [Line(seed, 170m)]));
    }

    [Fact]
    public async Task In_fixed_mode_the_last_sales_rate_is_not_a_kept_price()
    {
        var (db, seed) = await SeedAsync(suggestMode: SuggestSellingPriceMode.FixedSellingPrice);
        var sold = await CreateInvoiceAsync(db, seed, await SalesPricing.PricerAsync(db, seed.OrganizationId), [Line(seed, 180m)]);
        await ApproveInvoiceAsync(db, seed, sold.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateInvoiceAsync(db, seed, Clerk(), [Line(seed, 180m)]));
    }

    /// <summary>The draft a manager priced can still be edited by a clerk: what it already carried is not
    /// the clerk's change. Changing either price is.
    /// <para>Each request gets its own context, as in the Api: a refused update has already cleared the
    /// tracked lines, which a second call on the same context would read as the saved draft.</para></summary>
    [Fact]
    public async Task Editing_a_priced_draft_judges_only_what_the_edit_changes()
    {
        var name = Guid.NewGuid().ToString();
        var (db, seed) = await SeedAsync(databaseName: name);
        var draft = await CreateInvoiceAsync(
            db, seed, await SalesPricing.PricerAsync(db, seed.OrganizationId),
            [Line(seed, 120m, discountPct: 10m)], discountPct: 5m);

        await UpdateInvoiceAsync(
            TestAppDbContext.Create(name), seed, draft.Id, Clerk(), [Line(seed, 120m, discountPct: 10m, quantity: 3m)], 5m);

        var discount = await Assert.ThrowsAsync<ForbiddenException>(() => UpdateInvoiceAsync(
            TestAppDbContext.Create(name), seed, draft.Id, Clerk(), [Line(seed, 120m, discountPct: 12m)], 5m));
        var documentDiscount = await Assert.ThrowsAsync<ForbiddenException>(() => UpdateInvoiceAsync(
            TestAppDbContext.Create(name), seed, draft.Id, Clerk(), [Line(seed, 120m, discountPct: 10m)], 6m));
        var rate = await Assert.ThrowsAsync<ForbiddenException>(() => UpdateInvoiceAsync(
            TestAppDbContext.Create(name), seed, draft.Id, Clerk(), [Line(seed, 125m, discountPct: 10m)], 5m));

        Assert.Contains("(Sales.Invoice.Discount)", discount.Message);
        Assert.Contains("(Sales.Invoice.Discount)", documentDiscount.Message);
        Assert.Contains("(Sales.Invoice.ChangeRate)", rate.Message);
        var saved = await TestAppDbContext.Create(name).InvoiceLines.SingleAsync(x => x.InvoiceId == draft.Id);
        Assert.Equal((3m, 120m, 10m), (saved.Quantity, saved.Rate, saved.DiscountPct));
    }

    /// <summary>Charging the full price needs no key, even on a line the manager also repriced: the rate
    /// and the discount are carried separately.</summary>
    [Fact]
    public async Task Taking_a_discount_off_needs_no_key()
    {
        var (db, seed) = await SeedAsync();
        var draft = await CreateInvoiceAsync(
            db, seed, await SalesPricing.PricerAsync(db, seed.OrganizationId),
            [Line(seed, 120m, discountPct: 10m)], discountPct: 5m);

        await UpdateInvoiceAsync(db, seed, draft.Id, Clerk(), [Line(seed, 120m)], 0m);

        var line = await db.InvoiceLines.SingleAsync(x => x.InvoiceId == draft.Id);
        Assert.Equal(0m, line.DiscountPct);
    }

    /// <summary>One carried discounted line vouches for one line, so a clerk cannot duplicate it.</summary>
    [Fact]
    public async Task A_carried_discount_vouches_for_one_line_only()
    {
        var (db, seed) = await SeedAsync();
        var draft = await CreateInvoiceAsync(
            db, seed, await SalesPricing.PricerAsync(db, seed.OrganizationId), [Line(seed, 150m, discountPct: 10m)]);

        await Assert.ThrowsAsync<ForbiddenException>(() => UpdateInvoiceAsync(
            db, seed, draft.Id, Clerk(), [Line(seed, 150m, discountPct: 10m), Line(seed, 150m, discountPct: 10m)], 0m));
    }

    /// <summary>A quotation passes the same gate, which is what makes converting one safe: the clerk who
    /// converts a manager's quotation keeps its prices, and cannot change them on the way.</summary>
    [Fact]
    public async Task Converting_a_priced_quotation_carries_its_prices_and_nothing_else()
    {
        var name = Guid.NewGuid().ToString();
        var (db, seed) = await SeedAsync(databaseName: name);
        var quotation = await new CreateQuotationCommandHandler(db, await SalesPricing.PricerAsync(db, seed.OrganizationId))
            .Handle(
                new CreateQuotationCommand(
                    seed.OrganizationId, seed.CustomerId, Day, null, null,
                    [new QuotationLineInput(seed.ProductId, 2m, 120m, VatRate.NoVat, 10m)], 5m),
                CancellationToken.None);
        await new ApproveQuotationCommandHandler(db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(new ApproveQuotationCommand(seed.OrganizationId, quotation.Id), CancellationToken.None);

        var changed = await Assert.ThrowsAsync<ForbiddenException>(() => CreateInvoiceAsync(
            TestAppDbContext.Create(name), seed, Clerk(), [Line(seed, 120m, discountPct: 15m, quantity: 2m)], discountPct: 5m,
            referrerId: quotation.Id));
        Assert.Contains("(Sales.Invoice.Discount)", changed.Message);

        await CreateInvoiceAsync(
            TestAppDbContext.Create(name), seed, Clerk(), [Line(seed, 120m, discountPct: 10m, quantity: 2m)],
            discountPct: 5m, referrerId: quotation.Id);
    }

    [Fact]
    public async Task Quotations_and_sales_orders_pass_the_same_gate()
    {
        var (db, seed) = await SeedAsync();

        var quotation = await Assert.ThrowsAsync<ForbiddenException>(() =>
            new CreateQuotationCommandHandler(db, Clerk()).Handle(
                new CreateQuotationCommand(
                    seed.OrganizationId, seed.CustomerId, Day, null, null,
                    [new QuotationLineInput(seed.ProductId, 1m, 150m, VatRate.NoVat, 10m)]),
                CancellationToken.None));
        var salesOrder = await Assert.ThrowsAsync<ForbiddenException>(() =>
            new CreateSalesOrderCommandHandler(db, Clerk()).Handle(
                new CreateSalesOrderCommand(
                    seed.OrganizationId, seed.CustomerId, Day, null, null,
                    [new SalesOrderLineInput(seed.ProductId, 1m, 99m, VatRate.NoVat)]),
                CancellationToken.None));

        Assert.Contains("(Sales.Invoice.Discount)", quotation.Message);
        Assert.Contains("(Sales.Invoice.ChangeRate)", salesOrder.Message);
    }

    [Fact]
    public async Task Editing_a_quotation_is_held_to_the_same_rule()
    {
        var (db, seed) = await SeedAsync();
        var quotation = await new CreateQuotationCommandHandler(db, Clerk()).Handle(
            new CreateQuotationCommand(
                seed.OrganizationId, seed.CustomerId, Day, null, null,
                [new QuotationLineInput(seed.ProductId, 1m, 150m, VatRate.NoVat)]),
            CancellationToken.None);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() =>
            new UpdateQuotationCommandHandler(db, Clerk()).Handle(
                new UpdateQuotationCommand(
                    seed.OrganizationId, quotation.Id, seed.CustomerId, Day, null, null,
                    [new QuotationLineInput(seed.ProductId, 1m, 150m, VatRate.NoVat)], 3m),
                CancellationToken.None));

        Assert.Contains("(Sales.Invoice.Discount)", refused.Message);
    }

    /// <summary>Per location, as phase 32b scopes every Sales.Invoice key: the refusal names the branch,
    /// and the screen's read agrees with it.</summary>
    [Fact]
    public async Task A_grant_at_one_location_prices_there_and_nowhere_else()
    {
        var (db, seed) = await SeedAsync();
        var settings = await db.TenantSettings.SingleAsync(x => x.OrganizationId == seed.OrganizationId);
        settings.SetLocationSettings(LocationScopeMode.AllTransactions, false);
        var headOffice = BillingLocation.CreateHeadOffice(seed.OrganizationId);
        var branch = BillingLocation.Create(seed.OrganizationId, "BR1", "Lakeside", null, null);
        db.BillingLocations.AddRange(headOffice, branch);
        var userId = Guid.NewGuid();
        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(seed.OrganizationId, userId, MembershipRole.Member));
        db.RolePermissions.Add(
            RolePermission.Create(Guid.NewGuid(), Role.MemberId, PermissionKeys.InvoiceDiscount, true, headOffice.Id));
        await db.SaveChangesAsync();
        var user = new FakeCurrentUserService(userId);

        await CreateInvoiceAsync(db, seed, user, [Line(seed, 150m, discountPct: 10m)], locationId: headOffice.Id);
        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => CreateInvoiceAsync(
            db, seed, user, [Line(seed, 150m, discountPct: 10m)], locationId: branch.Id));

        Assert.Contains("(BR1.Sales.Invoice.Discount)", refused.Message);
        Assert.Equal(new SalesPricePowersDto(true, false, true), await PowersAsync(db, seed, user, headOffice.Id));
        Assert.Equal(new SalesPricePowersDto(true, false, true), await PowersAsync(db, seed, user, null));
        Assert.Equal(new SalesPricePowersDto(false, false, true), await PowersAsync(db, seed, user, branch.Id));
    }

    [Fact]
    public async Task The_screens_read_says_what_save_will_refuse()
    {
        var (db, seed) = await SeedAsync();

        Assert.Equal(new SalesPricePowersDto(false, false, false), await PowersAsync(db, seed, Clerk(), null));
        Assert.Equal(
            new SalesPricePowersDto(true, true, false),
            await PowersAsync(db, seed, await SalesPricing.PricerAsync(db, seed.OrganizationId), null));
    }

    /// <summary>A unit's own price is kept in that unit. Picking a unit used to drop its raw price into
    /// Rate, which on an inclusive tenant charged the VAT twice; the suggestion now goes through the price
    /// basis, and so does the gate, so the two agree.</summary>
    [Fact]
    public async Task A_secondary_units_suggested_price_is_a_kept_price_through_the_basis()
    {
        var (db, seed) = await SeedAsync(
            sellingPrice: 113m, vatRate: VatRate.ThirteenPercentVat, priceBasis: ProductPriceBasis.InclusiveOfVat);
        var box = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(seed.OrganizationId, "Box", "box"), CancellationToken.None);
        await new AddSecondaryUnitCommandHandler(db).Handle(
            new AddSecondaryUnitCommand(seed.OrganizationId, seed.ProductId, box.Id, 10m, 1130m, 900m),
            CancellationToken.None);

        var suggested = await new SuggestProductRateQueryHandler(db).Handle(
            new SuggestProductRateQuery(seed.OrganizationId, seed.ProductId, box.Id), CancellationToken.None);
        Assert.Equal(1000m, suggested.Rate);

        await CreateInvoiceAsync(
            db, seed, Clerk(), [Line(seed, 1000m, vatRate: VatRate.ThirteenPercentVat, unitId: box.Id)]);
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateInvoiceAsync(
            db, seed, Clerk(), [Line(seed, 1130m, vatRate: VatRate.ThirteenPercentVat, unitId: box.Id)]));
    }

    /// <summary>A line's rate is per unit entered (phase 52), so the last sale in boxes says nothing about
    /// a sale in pieces.</summary>
    [Fact]
    public async Task Recent_mode_reads_the_last_sale_in_the_lines_own_unit()
    {
        var (db, seed) = await SeedAsync(suggestMode: SuggestSellingPriceMode.RecentSellingPrice);
        var box = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(seed.OrganizationId, "Box", "box"), CancellationToken.None);
        await new AddSecondaryUnitCommandHandler(db).Handle(
            new AddSecondaryUnitCommand(seed.OrganizationId, seed.ProductId, box.Id, 10m, 1500m, 900m),
            CancellationToken.None);
        var sold = await CreateInvoiceAsync(
            db, seed, await SalesPricing.PricerAsync(db, seed.OrganizationId), [Line(seed, 1400m, unitId: box.Id)]);
        await ApproveInvoiceAsync(db, seed, sold.Id);

        var inPieces = await new SuggestProductRateQueryHandler(db).Handle(
            new SuggestProductRateQuery(seed.OrganizationId, seed.ProductId), CancellationToken.None);
        var inBoxes = await new SuggestProductRateQueryHandler(db).Handle(
            new SuggestProductRateQuery(seed.OrganizationId, seed.ProductId, box.Id), CancellationToken.None);

        Assert.Equal((150m, ProductRateSource.ProductSellingPrice), (inPieces.Rate, inPieces.Source));
        Assert.Equal((1400m, ProductRateSource.RecentSale), (inBoxes.Rate, inBoxes.Source));
    }

    // ---- helpers ----

    private static async Task<(IAppDbContext Db, CreditSeed Seed)> SeedAsync(
        decimal sellingPrice = 150m,
        VatRate vatRate = VatRate.NoVat,
        SuggestSellingPriceMode suggestMode = SuggestSellingPriceMode.FixedSellingPrice,
        ProductPriceBasis priceBasis = ProductPriceBasis.ExclusiveOfVat,
        string? databaseName = null)
    {
        var db = TestAppDbContext.Create(databaseName ?? Guid.NewGuid().ToString());
        var seed = await CreditLimitTestSeed.SeedAsync(
            db, sellingPrice: sellingPrice, vatRate: vatRate, suggestMode: suggestMode, priceBasis: priceBasis);
        return (db, seed);
    }

    private static FakeCurrentUserService Clerk() => new(Guid.NewGuid());

    private static async Task<FakeCurrentUserService> GrantedAsync(IAppDbContext db, CreditSeed seed, params string[] keys)
    {
        var userId = Guid.NewGuid();
        await PermissionGrantSeed.GrantAsync(db, seed.OrganizationId, userId, keys);
        return new FakeCurrentUserService(userId);
    }

    private static InvoiceLineInput Line(
        CreditSeed seed, decimal rate, decimal discountPct = 0m, decimal quantity = 1m,
        VatRate vatRate = VatRate.NoVat, Guid? unitId = null) =>
        new(seed.ProductId, quantity, rate, vatRate, discountPct, UnitId: unitId);

    private static Task<CreateInvoiceResult> CreateInvoiceAsync(
        IAppDbContext db, CreditSeed seed, ICurrentUserService user, IReadOnlyList<InvoiceLineInput> lines,
        decimal discountPct = 0m, string? currencyCode = null, decimal? exchangeRate = null, Guid? locationId = null,
        Guid? referrerId = null) =>
        new CreateInvoiceCommandHandler(db, user).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, Day, null, lines,
                referrerId is null ? null : DocumentType.Quotation, referrerId, discountPct)
            {
                CurrencyCode = currencyCode,
                ExchangeRate = exchangeRate,
                LocationId = locationId,
            },
            CancellationToken.None);

    private static Task<UpdateInvoiceResult> UpdateInvoiceAsync(
        IAppDbContext db, CreditSeed seed, Guid id, ICurrentUserService user, IReadOnlyList<InvoiceLineInput> lines,
        decimal discountPct) =>
        new UpdateInvoiceCommandHandler(db, user).Handle(
            new UpdateInvoiceCommand(seed.OrganizationId, id, seed.CustomerId, seed.WarehouseId, Day, null, lines, discountPct),
            CancellationToken.None);

    private static async Task ApproveInvoiceAsync(IAppDbContext db, CreditSeed seed, Guid id)
    {
        var stockLedgerService = new StockLedgerService(db);
        await new ApproveInvoiceCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
                new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService,
                new ContactCreditLimitPolicy(db))
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, id), CancellationToken.None);
    }

    private static Task<SalesPricePowersDto> PowersAsync(
        IAppDbContext db, CreditSeed seed, ICurrentUserService user, Guid? locationId) =>
        new GetSalesPricePowersQueryHandler(db, user).Handle(
            new GetSalesPricePowersQuery(seed.OrganizationId, DocumentType.Invoice, locationId), CancellationToken.None);
}
