using ErpApp.Application.Catalog.Queries.SuggestProductRate;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.CreditControl;

/// <summary>
/// Phase 31 -- <c>SuggestSellingPriceMode</c> and <c>ProductPriceBasis</c>, both dead since phase 2
/// and both silently decided in the browser (four Angular screens hardcoded
/// <c>rate: product.sellingPrice</c>, which is the Fixed branch).
/// </summary>
public class SuggestProductRateQueryHandlerTests
{
    [Fact]
    public async Task Fixed_mode_returns_the_products_own_selling_price()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(
            db, sellingPrice: 150m, suggestMode: SuggestSellingPriceMode.FixedSellingPrice);
        await SellAtAsync(db, seed, 900m);

        var result = await Handle(db, seed);

        Assert.Equal(150m, result.Rate);
        Assert.Equal(ProductRateSource.ProductSellingPrice, result.Source);
    }

    [Fact]
    public async Task Recent_mode_returns_the_last_approved_invoice_line_rate()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(
            db, sellingPrice: 150m, suggestMode: SuggestSellingPriceMode.RecentSellingPrice);
        await SellAtAsync(db, seed, 900m);

        var result = await Handle(db, seed);

        Assert.Equal(900m, result.Rate);
        Assert.Equal(ProductRateSource.RecentSale, result.Source);
    }

    /// <summary>A product that has never been sold has no recent price to suggest, so Recent falls
    /// back to the product's own -- otherwise the very first sale of anything would start at zero.</summary>
    [Fact]
    public async Task Recent_mode_falls_back_to_the_product_price_when_nothing_has_been_sold()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(
            db, sellingPrice: 150m, suggestMode: SuggestSellingPriceMode.RecentSellingPrice);

        var result = await Handle(db, seed);

        Assert.Equal(150m, result.Rate);
        Assert.Equal(ProductRateSource.ProductSellingPrice, result.Source);
    }

    /// <summary>
    /// Inclusive of VAT means the product's stored price already contains the tax, so the suggested
    /// line rate is back-calculated. 113 inclusive at 13% is 100 exclusive -- and the line still
    /// stores an exclusive rate, because the basis is about reading the product's price, never about
    /// what a document holds.
    /// </summary>
    [Fact]
    public async Task Inclusive_of_vat_back_calculates_the_products_price()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(
            db, sellingPrice: 113m, vatRate: VatRate.ThirteenPercentVat,
            suggestMode: SuggestSellingPriceMode.FixedSellingPrice, priceBasis: ProductPriceBasis.InclusiveOfVat);

        var result = await Handle(db, seed);

        Assert.Equal(100m, result.Rate);
        Assert.Equal(VatRate.ThirteenPercentVat, result.VatRate);
    }

    /// <summary>A NoVat product divides by 1, so the two bases agree -- pinned because the handler
    /// deliberately has no branch on the rate and this is what makes that safe.</summary>
    [Fact]
    public async Task Inclusive_of_vat_leaves_a_non_vat_product_untouched()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(
            db, sellingPrice: 113m, vatRate: VatRate.NoVat,
            suggestMode: SuggestSellingPriceMode.FixedSellingPrice, priceBasis: ProductPriceBasis.InclusiveOfVat);

        var result = await Handle(db, seed);

        Assert.Equal(113m, result.Rate);
    }

    /// <summary>
    /// The basis applies to the product's own price and never to a recent sale's rate: an
    /// <c>InvoiceLine.Rate</c> is already VAT-exclusive, so dividing it again would strip 13% from a
    /// figure that never carried it.
    /// </summary>
    [Fact]
    public async Task Inclusive_of_vat_is_never_applied_to_a_recent_sale_rate()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(
            db, sellingPrice: 113m, vatRate: VatRate.ThirteenPercentVat,
            suggestMode: SuggestSellingPriceMode.RecentSellingPrice, priceBasis: ProductPriceBasis.InclusiveOfVat);
        await SellAtAsync(db, seed, 900m);

        var result = await Handle(db, seed);

        Assert.Equal(900m, result.Rate);
    }

    private static Task<SuggestedProductRateDto> Handle(IAppDbContext db, CreditSeed seed) =>
        new SuggestProductRateQueryHandler(db).Handle(
            new SuggestProductRateQuery(seed.OrganizationId, seed.ProductId), CancellationToken.None);

    private static async Task SellAtAsync(IAppDbContext db, CreditSeed seed, decimal rate)
    {
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, new DateOnly(2026, 1, 10), null,
                [new InvoiceLineInput(seed.ProductId, 1m, rate, VatRate.NoVat)]),
            CancellationToken.None);

        var stockLedgerService = new StockLedgerService(db);
        await new ApproveInvoiceCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
                new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService,
                new ContactCreditLimitPolicy(db))
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }
}
