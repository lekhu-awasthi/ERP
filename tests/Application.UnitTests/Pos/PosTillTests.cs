using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Commands.PrintPosReceipt;
using ErpApp.Application.Pos.Queries.GetPosTill;
using ErpApp.Application.Pos.Queries.ListPosProducts;
using ErpApp.Application.Pos.Queries.ListPosSessionSales;
using ErpApp.Application.Pos.Queries.ListPosTills;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 62 -- what the till screen reads (its launcher, its own settings, its product grid, a
/// session's sales), the abbreviated-tax-invoice rule (Decision A) and the receipt's print count
/// (Decision B). On phase 61's till: 10% service charge, rounding on, Cash and Card, Momo 200 and
/// Coke 60, both 13% VAT.
/// </summary>
public class PosTillTests
{
    // ---- The launcher ---------------------------------------------------------------------------

    [Fact]
    public async Task The_launcher_lists_the_till_and_the_cashiers_own_open_session_there()
    {
        var till = await PosTestTill.CreateAsync();

        var before = await ListTillsAsync(till);
        var row = Assert.Single(before);
        Assert.Equal(till.Location.Id, row.LocationId);
        Assert.Equal(PosMode.Retail, row.PosMode);
        Assert.Null(row.MySessionId);

        var session = await till.OpenAsync();

        var after = Assert.Single(await ListTillsAsync(till));
        Assert.Equal(session.Id, after.MySessionId);
        Assert.Equal(session.Code, after.MySessionCode);
    }

    [Fact]
    public async Task A_till_the_cashier_could_not_open_a_drawer_at_is_not_offered()
    {
        // Opening needs Sales.Invoice.Create at the location (phase 61 Decision F). Granted only at a
        // second branch, the cashier is shown that branch and not this one.
        var till = await PosTestTill.CreateAsync(grantedKeys: [PermissionKeys.PosSessionOperate]);

        var branch = BillingLocation.Create(till.OrganizationId, "1002", "Lakeside", "Pokhara", null);
        branch.SetPosMode(PosMode.Retail);
        till.Db.BillingLocations.Add(branch);
        till.Db.RolePermissions.Add(
            RolePermission.Create(Guid.NewGuid(), Role.AdminId, PermissionKeys.InvoiceCreate, true, branch.Id));
        await till.Db.SaveChangesAsync();

        var row = Assert.Single(await ListTillsAsync(till));
        Assert.Equal(branch.Id, row.LocationId);
    }

    [Fact]
    public async Task A_location_whose_mode_the_tenant_is_not_entitled_to_is_not_offered()
    {
        // The fixture's tenant holds POS Retail only; a Restaurant till there can be configured
        // (phase 60) but never opened (PosTill.LoadAsync), so the launcher leaves it out.
        var till = await PosTestTill.CreateAsync();

        var restaurant = BillingLocation.Create(till.OrganizationId, "1003", "Ground Floor", "Thamel", null);
        restaurant.SetPosMode(PosMode.Restaurant);
        till.Db.BillingLocations.Add(restaurant);
        await till.Db.SaveChangesAsync();

        Assert.DoesNotContain(await ListTillsAsync(till), x => x.LocationId == restaurant.Id);
    }

    // ---- The till's own read ------------------------------------------------------------------

    [Fact]
    public async Task The_till_offers_exactly_the_modes_a_sale_would_accept()
    {
        var till = await PosTestTill.CreateAsync();

        // Linked but unusable: inactive, and with no account. A sale refuses both (phase 61), so the
        // payment screen must not offer either.
        var inactive = PaymentMode.Create(till.OrganizationId, "Old Card", kind: PaymentModeKind.Card, accountId: till.BankAccountId);
        inactive.Update("Old Card", false, false, PaymentModeKind.Card, till.BankAccountId);
        var unaccounted = PaymentMode.Create(till.OrganizationId, "Voucher", kind: PaymentModeKind.Other, accountId: null);
        till.Db.PaymentModes.AddRange(inactive, unaccounted);
        till.Db.PosLocationPaymentModes.AddRange(
            PosLocationPaymentMode.Create(till.OrganizationId, till.Location.Id, inactive.Id),
            PosLocationPaymentMode.Create(till.OrganizationId, till.Location.Id, unaccounted.Id));
        await till.Db.SaveChangesAsync();

        var dto = await GetTillAsync(till);

        Assert.Equal(["Cash", "Card"], dto.PaymentModes.Select(x => x.Name));
        Assert.Equal(PaymentModeKind.Cash, dto.PaymentModes[0].Kind);
        Assert.True(dto.ServiceChargeEnabled);
        Assert.Equal(10m, dto.ServiceChargeRate);
        Assert.True(dto.RoundOffEnabled);
        Assert.Equal(till.WalkInId, dto.WalkInCustomer!.Id);
        Assert.True(dto.IsVatRegistered);
        Assert.Equal([PosTab.Retail, PosTab.Delivery], dto.AvailableTabs);
        Assert.Equal(PosTab.Retail, dto.DefaultTab);
    }

    [Fact]
    public async Task The_till_says_in_advance_whether_its_cashier_may_leave_a_bill_on_credit()
    {
        var withApprove = await PosTestTill.CreateAsync();
        Assert.True((await GetTillAsync(withApprove)).CanSellOnCredit);

        var cashOnly = await PosTestTill.CreateAsync(
            grantedKeys: [PermissionKeys.InvoiceCreate, PermissionKeys.PosSessionOperate]);
        Assert.False((await GetTillAsync(cashOnly)).CanSellOnCredit);
    }

    [Fact]
    public async Task The_till_refuses_a_location_that_runs_no_till_before_painting_anything()
    {
        var till = await PosTestTill.CreateAsync();
        var plain = BillingLocation.Create(till.OrganizationId, "1004", "Back Office", "Thamel", null);
        till.Db.BillingLocations.Add(plain);
        await till.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            new GetPosTillQueryHandler(till.Db, till.CurrentUser())
                .Handle(new GetPosTillQuery(till.OrganizationId, plain.Id), CancellationToken.None));
    }

    // ---- The product grid ---------------------------------------------------------------------

    [Fact]
    public async Task The_grid_shows_only_what_the_till_may_sell()
    {
        var till = await PosTestTill.CreateAsync();
        var unsellable = await CreateProductAsync(till, "Staff Meal", availableForSale: false);

        var page = await ListProductsAsync(till);

        Assert.Contains(page.Items, x => x.Id == till.MomoId);
        Assert.Contains(page.Items, x => x.Id == till.CokeId);
        Assert.DoesNotContain(page.Items, x => x.Id == unsellable);

        // Every category tab the till offers holds at least one of these.
        var categories = (await GetTillAsync(till)).Categories.Select(x => x.Id).ToHashSet();
        Assert.All(page.Items, x => Assert.Contains(x.CategoryId, categories));
    }

    [Fact]
    public async Task A_scanned_code_matches_a_barcode_exactly_and_nothing_partial()
    {
        var till = await PosTestTill.CreateAsync();
        var water = await CreateProductAsync(till, "Mineral Water 1L", barcode: "8901234567890");
        await CreateProductAsync(till, "Mineral Water 500ml", barcode: "8901234567891");

        var exact = await ListProductsAsync(till, code: "8901234567890");
        Assert.Equal(water, Assert.Single(exact.Items).Id);

        // A prefix is a typed search, not a scan.
        Assert.Empty((await ListProductsAsync(till, code: "890123456789")).Items);
        Assert.Equal(2, (await ListProductsAsync(till, search: "890123456789")).Items.Count);
    }

    [Fact]
    public async Task A_vat_inclusive_catalogue_is_rung_up_at_its_exclusive_rate_in_every_unit()
    {
        var till = await PosTestTill.CreateAsync();
        var settings = await till.Db.TenantSettings.SingleAsync(x => x.OrganizationId == till.OrganizationId);
        settings.UpdateSettings(
            settings.SuggestSellingPriceMode, ProductPriceBasis.InclusiveOfVat, settings.InventoryTrackingMode,
            settings.NegativeCashBalanceAction, settings.NegativeStockBalanceAction, settings.CreditLimitExceedsAction);

        // A crate of 24 Cokes, priced 1,356 inclusive (= 1,200 + 13%).
        var crateUnit = UnitOfMeasurement.Create(till.OrganizationId, "Crate", "crt");
        till.Db.UnitsOfMeasurement.Add(crateUnit);
        var coke = await till.Db.Products.Include(x => x.SecondaryUnits).SingleAsync(x => x.Id == till.CokeId);
        till.Db.ProductSecondaryUnits.Add(coke.AddSecondaryUnit(crateUnit.Id, 24m, 1356m, 960m));
        await till.Db.SaveChangesAsync();

        var row = Assert.Single((await ListProductsAsync(till, search: "Coke")).Items);

        // 60 typed as VAT-inclusive is 53.10 exclusive -- what the sale command must be sent as Rate.
        Assert.Equal(53.10m, row.Rate);
        Assert.Equal(2, row.Units.Count);
        Assert.Equal((row.Units[0].UnitId, 1m, 53.10m), (row.Units[0].UnitId, row.Units[0].ConversionRate, row.Units[0].Rate));
        Assert.Equal(("crt", 24m, 1200m), (row.Units[1].ShortName, row.Units[1].ConversionRate, row.Units[1].Rate));
    }

    // ---- Decision A: which bills are abbreviated tax invoices -----------------------------------

    [Fact]
    public async Task A_walk_in_bill_within_the_limit_is_abbreviated_where_the_permission_is_recorded()
    {
        var till = await PosTestTill.CreateAsync();
        await AllowAbbreviatedAsync(till);
        var session = await till.OpenAsync();

        var sale = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]);

        Assert.True(sale.IsAbbreviatedTaxInvoice);
        Assert.Equal(InvoiceHeading.AbbreviatedTaxInvoice, (await PrintAsync(till, sale.Id)).Title);
    }

    [Fact]
    public async Task A_named_customer_always_gets_the_full_tax_invoice()
    {
        // Rule 18: a customer who asks for a full tax invoice must be given one, and naming the
        // customer at the till is how a cashier asks.
        var till = await PosTestTill.CreateAsync();
        await AllowAbbreviatedAsync(till);
        var session = await till.OpenAsync();

        var sale = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)], contactId: till.Seed.CustomerId);

        Assert.False(sale.IsAbbreviatedTaxInvoice);
        var receipt = await PrintAsync(till, sale.Id);
        Assert.Equal(InvoiceHeading.TaxInvoice, receipt.Title);
        Assert.False(receipt.IsWalkIn);
        Assert.Equal("301234567", receipt.CustomerPan);
    }

    [Fact]
    public async Task A_bill_over_ten_thousand_rupees_is_never_abbreviated()
    {
        var till = await PosTestTill.CreateAsync();
        await AllowAbbreviatedAsync(till);
        var session = await till.OpenAsync();

        // 45 momo: 9,000 + SC 900 + VAT 1,287 = 11,187.
        var over = await till.SellAsync(session.Id, [till.Momo(45)], [till.Card(11187m)]);
        Assert.False(over.IsAbbreviatedTaxInvoice);

        // Exactly at the limit is allowed: the rule refuses "more than" it.
        Assert.Equal(10_000m, Invoice.AbbreviatedTaxInvoiceLimit);
    }

    [Fact]
    public async Task Without_the_recorded_permission_every_bill_is_a_full_tax_invoice()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();

        var sale = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]);

        Assert.False(sale.IsAbbreviatedTaxInvoice);
        Assert.Equal(InvoiceHeading.TaxInvoice, (await PrintAsync(till, sale.Id)).Title);
    }

    [Fact]
    public async Task An_unregistered_seller_issues_an_invoice_and_never_a_tax_invoice()
    {
        var till = await PosTestTill.CreateAsync(vatRegistered: false);
        await AllowAbbreviatedAsync(till);
        var session = await till.OpenAsync();

        var sale = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]);

        Assert.False(sale.IsAbbreviatedTaxInvoice);
        Assert.Equal(InvoiceHeading.Invoice, (await PrintAsync(till, sale.Id)).Title);
    }

    // ---- Decision B: the original, then marked copies -----------------------------------------

    [Fact]
    public async Task The_first_print_is_the_original_and_every_later_one_a_numbered_copy()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);

        var original = await PrintAsync(till, sale.Id);
        var copy = await PrintAsync(till, sale.Id);
        var another = await PrintAsync(till, sale.Id, userId: till.UserId);

        Assert.Equal([1, 2, 3], new[] { original.PrintNumber, copy.PrintNumber, another.PrintNumber });
        Assert.Equal(3, await till.Db.InvoicePrints.CountAsync(x => x.InvoiceId == sale.Id));
        Assert.Equal("Sita Cashier", copy.PrintedByName);

        var listed = Assert.Single(await ListSalesAsync(till, session.Id));
        Assert.Equal(3, listed.PrintCount);
    }

    [Fact]
    public async Task The_receipt_prints_service_charge_and_round_off_as_lines_of_their_own()
    {
        // Phase 59 defect 3: the vendor folded both into Taxable and printed neither.
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);

        var receipt = await PrintAsync(till, sale.Id);

        Assert.Equal(520m, receipt.SubTotal);
        Assert.Equal(40m, receipt.ServiceCharge);
        Assert.Equal(560m, receipt.TaxableAmount);
        Assert.Equal(0m, receipt.NonTaxableAmount);
        Assert.Equal(72.80m, receipt.Vat);
        Assert.Equal(0.20m, receipt.RoundOff);
        Assert.Equal(633m, receipt.GrandTotal);
        Assert.Equal(receipt.SubTotal + receipt.ServiceCharge + receipt.Vat + receipt.RoundOff, receipt.GrandTotal);
        Assert.Equal("Rupees Six Hundred Thirty Three Only", receipt.AmountInWords);
        Assert.Equal((1000m, 367m, 0m), (receipt.Tendered, receipt.ChangeAmount, receipt.CreditAmount));
        Assert.Equal("Cash", Assert.Single(receipt.Tenders).PaymentModeName);
        Assert.Equal(session.Code, receipt.SessionCode);
        Assert.Equal("Sita Cashier", receipt.CashierName);
        Assert.Equal("601234567", receipt.SellerPan);
    }

    [Fact]
    public async Task The_receipt_shows_the_discount_the_lines_took_off_their_gross()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();

        // Coke at 60 with 10% off the line: gross 60, amount 54, VAT 7.02, 61.02 rounds to 61.
        var sale = await till.SellAsync(
            session.Id, [till.Coke(1) with { DiscountPct = 10m }], [till.Cash(61m)]);

        var receipt = await PrintAsync(till, sale.Id);

        Assert.Equal((60m, 6m, 54m), (receipt.GrossAmount, receipt.DiscountAmount, receipt.SubTotal));
        Assert.Equal(receipt.GrossAmount - receipt.DiscountAmount, receipt.SubTotal);
    }

    [Fact]
    public async Task A_voided_sale_prints_nothing()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]);
        await till.VoidAsync(sale.Id);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => PrintAsync(till, sale.Id));
        Assert.Contains("voided", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, await till.Db.InvoicePrints.CountAsync());

        var listed = Assert.Single(await ListSalesAsync(till, session.Id));
        Assert.Equal(InvoiceStatus.Void, listed.Status);
    }

    [Fact]
    public async Task An_erp_invoice_has_no_till_receipt()
    {
        var till = await PosTestTill.CreateAsync();
        var (invoiceId, _) = await InventoryReportSeed.SellAsync(till.Db, till.Seed, PosTestTill.Today, 1m, 60m, till.CokeId);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => PrintAsync(till, invoiceId));
        Assert.Contains("not rung up at a till", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_invoice_is_a_404_not_a_print()
    {
        var till = await PosTestTill.CreateAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => PrintAsync(till, Guid.NewGuid()));
    }

    [Fact]
    public async Task A_sessions_sales_list_newest_first_with_what_each_left_on_credit()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var first = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]);
        var second = await till.SellAsync(
            session.Id, [till.Momo(1)], [till.Card(100m)], contactId: till.Seed.CustomerId);

        var sales = await ListSalesAsync(till, session.Id);

        Assert.Equal(2, sales.Count);
        Assert.Contains(sales, x => x.InvoiceId == first.Id && x.IsWalkIn && x.CreditAmount == 0m);
        Assert.Contains(sales, x => x.InvoiceId == second.Id && !x.IsWalkIn && x.CreditAmount == 149m && x.CustomerName == "Acme Retail");
        Assert.All(sales, x => Assert.Equal(0, x.PrintCount));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static Task<IReadOnlyList<PosTillSummaryDto>> ListTillsAsync(PosTestTill till) =>
        new ListPosTillsQueryHandler(till.Db, till.CurrentUser())
            .Handle(new ListPosTillsQuery(till.OrganizationId), CancellationToken.None);

    private static Task<PosTillDto> GetTillAsync(PosTestTill till) =>
        new GetPosTillQueryHandler(till.Db, till.CurrentUser())
            .Handle(new GetPosTillQuery(till.OrganizationId, till.Location.Id), CancellationToken.None);

    private static Task<PagedResult<PosProductDto>> ListProductsAsync(
        PosTestTill till, string? search = null, string? code = null) =>
        new ListPosProductsQueryHandler(till.Db).Handle(
            new ListPosProductsQuery(till.OrganizationId, till.Location.Id, search, code), CancellationToken.None);

    private static Task<IReadOnlyList<PosSessionSaleDto>> ListSalesAsync(PosTestTill till, Guid sessionId) =>
        new ListPosSessionSalesQueryHandler(till.Db, till.CurrentUser())
            .Handle(new ListPosSessionSalesQuery(till.OrganizationId, sessionId), CancellationToken.None);

    private static Task<PosReceiptDto> PrintAsync(PosTestTill till, Guid invoiceId, Guid? userId = null) =>
        new PrintPosReceiptCommandHandler(till.Db, till.CurrentUser(userId))
            .Handle(new PrintPosReceiptCommand(till.OrganizationId, invoiceId), CancellationToken.None);

    private static async Task<Guid> CreateProductAsync(
        PosTestTill till, string name, bool availableForSale = true, string? barcode = null)
    {
        var unit = await till.Db.Products.Where(x => x.Id == till.CokeId).Select(x => x.PrimaryUnitId).SingleAsync();
        var created = await new CreateProductCommandHandler(till.Db, till.Seed.NumberGenerator).Handle(
            new CreateProductCommand(
                till.OrganizationId, ProductType.Service, name, till.Seed.CategoryId, unit, null, availableForSale, 25m, 0m,
                VatRate.ThirteenPercentVat, 0, false, Barcode: barcode),
            CancellationToken.None);
        return created.Id;
    }

    private static async Task AllowAbbreviatedAsync(PosTestTill till)
    {
        var settings = await till.Db.PosLocationSettings.SingleAsync();
        settings.Update(
            PosMode.Retail, true, 10m, null, true, null, false, PosLocationSettings.DefaultDenominations, null,
            true, true, true, false, abbreviatedTaxInvoiceEnabled: true);
        await till.Db.SaveChangesAsync();
    }
}
