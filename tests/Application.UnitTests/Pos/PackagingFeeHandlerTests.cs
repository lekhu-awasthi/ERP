using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.UpdateProduct;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Commands.UpdatePosLocationSettings;
using ErpApp.Application.Pos.Commands.UpdatePosOrderTakeAway;
using ErpApp.Application.Pos.Queries.GetPosLocationSettings;
using ErpApp.Application.Pos.Queries.GetPosSession;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 70 -- the packaging fee through the handlers: charged on a parcel and nothing else, posted to its own
/// account (location, then tenant default, then a 409), given back by a refund, counted in the session's
/// takings, frozen on a restaurant line, and set from the settings screen without an absent field switching it.
/// Phase 61's till: Chicken Momo (200, service charge) and Coke 250ml (60), both 13% VAT, rounding on.
/// </summary>
public class PackagingFeeHandlerTests
{
    private static readonly PackagingFee TwentyABox = PackagingFee.Create(PackagingFeeBasis.PerUnit, 20m);

    private static async Task SetFeeAsync(PosTestTill till, PackagingFee fee, Guid? accountId = null)
    {
        var settings = await till.Db.PosLocationSettings.SingleAsync();
        settings.Update(
            till.Location.PosMode, settings.ServiceChargeEnabled, settings.ServiceChargeRate, settings.ServiceChargeAccountId,
            settings.ServiceChargeOnTakeAway, fee, accountId, settings.RoundOffEnabled, settings.RoundOffAccountId,
            settings.CashVerificationRequired, settings.Denominations, settings.DefaultTab, settings.PrintEstimateBill,
            settings.PrintInvoice, settings.PrintCreditNote, settings.PrintKot, settings.AbbreviatedTaxInvoiceEnabled);
        await till.Db.SaveChangesAsync();
    }

    private static Task<CreatePosSaleResult> SellAsync(
        PosTestTill till, Guid sessionId, PosTab orderType, IReadOnlyList<PosSaleLineInput> lines, decimal cash) =>
        till.SaleHandler().Handle(
            new CreatePosSaleCommand(
                till.OrganizationId, sessionId, till.Location.Id, lines, [till.Cash(cash)], 0m, null, till.Seed.WarehouseId,
                orderType),
            CancellationToken.None);

    private static async Task<List<InvoiceLine>> LinesOfAsync(PosTestTill till, Guid invoiceId) =>
        await till.Db.InvoiceLines.Where(x => x.InvoiceId == invoiceId).ToListAsync();

    [Fact]
    public async Task A_delivery_sale_charges_the_fee_inside_the_vat_base_and_posts_it_to_its_own_account()
    {
        var till = await PosTestTill.CreateAsync();
        await SetFeeAsync(till, TwentyABox);
        var session = await till.OpenAsync();

        // Momo 400 + 40 + 13% of 440 = 497.20, and no service charge on a parcel; Coke 60 + 20 + 13% of 80 = 90.40.
        var sale = await SellAsync(till, session.Id, PosTab.Delivery, [till.Momo(2), till.Coke(1)], 588m);

        Assert.Equal((60m, 0m, 588m), (sale.PackagingFee, sale.ServiceCharge, sale.GrandTotal));
        var lines = await LinesOfAsync(till, sale.Id);
        var momo = lines.Single(x => x.ProductId == till.MomoId);
        Assert.Equal((400m, 0m, 40m, 57.20m), (momo.Amount, momo.ServiceChargeAmount, momo.PackagingFeeAmount, momo.VatAmount));
        Assert.Equal(TwentyABox, momo.PackagingFee);
        Assert.Equal(20m, lines.Single(x => x.ProductId == till.CokeId).PackagingFeeAmount);

        // The tenant default took the fee, and the paid sale leaves nothing owed.
        Assert.Equal(-60m, await till.BalanceAsync(till.PackagingFeeAccountId));
        Assert.Equal(0m, await till.BalanceAsync(await till.ReceivableAccountIdAsync()));

        // The session's takings carry it, and still add up.
        var view = await new GetPosSessionQueryHandler(till.Db, till.CurrentUser()).Handle(
            new GetPosSessionQuery(till.OrganizationId, session.Id), CancellationToken.None);
        var s = view.Sales;
        Assert.Equal(60m, s.PackagingFee);
        Assert.Equal(588m, s.GrandTotal);
        Assert.Equal(s.GrandTotal, s.SubTotal + s.ServiceCharge + s.PackagingFee + s.Vat + s.RoundOff);
        Assert.Equal(520m, s.Taxable);
        Assert.Equal(0m, s.NonTaxable);
    }

    [Fact]
    public async Task A_counter_sale_carries_no_fee_and_neither_does_a_product_exempted_from_it()
    {
        var till = await PosTestTill.CreateAsync();
        await SetFeeAsync(till, TwentyABox);
        var coke = await till.Db.Products.SingleAsync(x => x.Id == till.CokeId);
        coke.SetPackagingFeeApplicable(false);
        await till.Db.SaveChangesAsync();
        var session = await till.OpenAsync();

        var counter = await SellAsync(till, session.Id, PosTab.Retail, [till.Momo(1)], 249m);
        Assert.Equal(0m, counter.PackagingFee);

        var parcel = await SellAsync(till, session.Id, PosTab.Delivery, [till.Momo(1), till.Coke(1)], 316m);
        var lines = await LinesOfAsync(till, parcel.Id);
        Assert.Equal(20m, lines.Single(x => x.ProductId == till.MomoId).PackagingFeeAmount);
        Assert.Equal(0m, lines.Single(x => x.ProductId == till.CokeId).PackagingFeeAmount);
        Assert.Equal(PackagingFee.None, lines.Single(x => x.ProductId == till.CokeId).PackagingFee);
    }

    [Fact]
    public async Task The_locations_account_wins_and_a_fee_with_no_account_anywhere_is_refused_before_anything_is_written()
    {
        var till = await PosTestTill.CreateAsync();
        await SetFeeAsync(till, TwentyABox, till.VegetablesAccountId);
        var session = await till.OpenAsync();

        await SellAsync(till, session.Id, PosTab.Delivery, [till.Momo(1)], 249m);
        Assert.Equal(-20m, await till.BalanceAsync(till.VegetablesAccountId));
        Assert.Equal(0m, await till.BalanceAsync(till.PackagingFeeAccountId));

        await SetFeeAsync(till, TwentyABox, accountId: null);
        var settings = await till.Db.TenantSettings.SingleAsync(x => x.OrganizationId == till.OrganizationId);
        settings.SetPosDefaults(till.ServiceChargeAccountId, till.RoundingAccountId, till.OverShortAccountId, null);
        await till.Db.SaveChangesAsync();
        var invoicesBefore = await till.Db.Invoices.CountAsync();

        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            SellAsync(till, session.Id, PosTab.Delivery, [till.Momo(1)], 249m));
        Assert.Contains("packaging fee", refused.Message, StringComparison.Ordinal);
        Assert.Equal(invoicesBefore, await till.Db.Invoices.CountAsync());

        // A sale with no fee in it never asks for the account.
        Assert.Equal(0m, (await SellAsync(till, session.Id, PosTab.Retail, [till.Momo(1)], 249m)).PackagingFee);
    }

    [Fact]
    public async Task A_refund_gives_the_fee_back_out_of_its_account()
    {
        var till = await PosTestTill.CreateAsync();
        await SetFeeAsync(till, TwentyABox);
        var session = await till.OpenAsync();
        var sale = await SellAsync(till, session.Id, PosTab.Delivery, [till.Momo(1)], 249m);

        var preview = await till.PreviewRefundAsync(session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.MomoId, 1m)]);
        Assert.Equal(20m, preview.PackagingFee);
        Assert.Equal(20m, Assert.Single(preview.Lines).PackagingFeeAmount);

        var refund = await till.RefundAsync(
            session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.MomoId, 1m)], [till.Cash(249m)]);

        Assert.Equal(20m, refund.PackagingFee);
        Assert.Equal(249m, refund.GrandTotal);
        Assert.Equal(0m, await till.BalanceAsync(till.PackagingFeeAccountId));
        var receipt = await till.PrintRefundAsync(refund.Id);
        Assert.Equal(20m, receipt.PackagingFee);
    }

    // ---- The restaurant ----------------------------------------------------------------------------

    [Fact]
    public async Task A_take_away_order_freezes_the_fee_when_it_is_sent_and_bills_it_after_the_setting_changes()
    {
        var r = await PosTestRestaurant.CreateAsync();
        await SetFeeAsync(r.Till, TwentyABox);

        var order = await r.OpenAsync(PosTab.TakeAway, [r.Momo(2), r.Coke(1)]);
        Assert.Equal(60m, order.PackagingFee);
        Assert.Equal(0m, order.ServiceCharge);
        Assert.Equal(587.60m, order.Total);

        // A later change of the setting reprices nothing already sent.
        await SetFeeAsync(r.Till, PackagingFee.Create(PackagingFeeBasis.Percent, 5m));
        Assert.Equal(60m, (await r.GetAsync(order.Id)).PackagingFee);

        var preview = await r.PreviewAsync(order.Id, PosOrderSplit.Whole);
        Assert.Equal((60m, 588m), (preview.PackagingFee, preview.Total));

        var session = await r.Till.OpenAsync();
        var bill = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Whole, [r.Till.Cash(588m)]);
        Assert.Equal((60m, 588m), (bill.PackagingFee, bill.GrandTotal));
        Assert.Equal(-60m, await r.Till.BalanceAsync(r.Till.PackagingFeeAccountId));
    }

    [Fact]
    public async Task A_parcel_marked_at_the_table_takes_the_fee_unless_its_product_is_exempt()
    {
        var r = await PosTestRestaurant.CreateAsync();
        await SetFeeAsync(r.Till, TwentyABox);
        var coke = await r.Till.Db.Products.SingleAsync(x => x.Id == r.Till.CokeId);
        coke.SetPackagingFeeApplicable(false);
        await r.Till.Db.SaveChangesAsync();

        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        Assert.Equal(0m, order.PackagingFee);

        Task<PosOrderDto> MarkAsync(string product) =>
            new UpdatePosOrderTakeAwayCommandHandler(r.Till.Db, r.CurrentUser()).Handle(
                new UpdatePosOrderTakeAwayCommand(
                    r.OrganizationId, order.Id, order.Lines.Single(x => x.ProductName == product && !x.IsTakeAway).Id, 1m),
                CancellationToken.None);

        await MarkAsync("Chicken Momo");
        var marked = await MarkAsync("Coke 250ml");

        var momoParcel = marked.Lines.Single(x => x.IsTakeAway && x.ProductName == "Chicken Momo");
        var cokeParcel = marked.Lines.Single(x => x.IsTakeAway && x.ProductName == "Coke 250ml");
        // 200 + 20 service charge (kept on take-away by default) + 20 fee + 13% of 240.
        Assert.Equal((20m, 20m, 271.20m), (momoParcel.ServiceChargeAmount, momoParcel.PackagingFeeAmount, momoParcel.Total));
        Assert.Equal(0m, cokeParcel.PackagingFeeAmount);
        Assert.Equal(20m, marked.PackagingFee);
    }

    // ---- The settings screen ------------------------------------------------------------------------

    private static UpdatePosLocationSettingsCommand Save(
        PosTestTill till, PackagingFeeBasis? basis, decimal value, Guid? accountId) => new(
        till.OrganizationId, till.Location.Id,
        ServiceChargeEnabled: true, ServiceChargeRate: 10m, ServiceChargeAccountId: null, ServiceChargeOnTakeAway: true,
        PackagingFeeBasis: basis, PackagingFeeValue: value, PackagingFeeAccountId: accountId,
        RoundOffEnabled: true, RoundOffAccountId: null, CashVerificationRequired: false,
        Denominations: PosLocationSettings.DefaultDenominations, DefaultTab: null, PrintEstimateBill: true, PrintInvoice: true,
        PrintCreditNote: true, PrintKot: false, AbbreviatedTaxInvoiceEnabled: false);

    [Fact]
    public async Task The_settings_save_a_fee_and_an_absent_basis_leaves_it_as_it_was()
    {
        var till = await PosTestTill.CreateAsync();
        var handler = new UpdatePosLocationSettingsCommandHandler(till.Db);

        var saved = await handler.Handle(Save(till, PackagingFeeBasis.Percent, 5m, till.VegetablesAccountId), CancellationToken.None);
        Assert.Equal((PackagingFeeBasis.Percent, 5m, till.VegetablesAccountId), (saved.PackagingFeeBasis, saved.PackagingFeeValue, saved.PackagingFeeAccountId));

        var untouched = await handler.Handle(Save(till, null, 0m, null), CancellationToken.None);
        Assert.Equal((PackagingFeeBasis.Percent, 5m, till.VegetablesAccountId), (untouched.PackagingFeeBasis, untouched.PackagingFeeValue, untouched.PackagingFeeAccountId));

        var off = await handler.Handle(Save(till, PackagingFeeBasis.None, 99m, till.VegetablesAccountId), CancellationToken.None);
        Assert.Equal((PackagingFeeBasis.None, 0m, (Guid?)null), (off.PackagingFeeBasis, off.PackagingFeeValue, off.PackagingFeeAccountId));

        var read = await new GetPosLocationSettingsQueryHandler(till.Db).Handle(
            new GetPosLocationSettingsQuery(till.OrganizationId, till.Location.Id), CancellationToken.None);
        Assert.Equal(PackagingFeeBasis.None, read.PackagingFeeBasis);
    }

    [Theory]
    [InlineData(PackagingFeeBasis.Percent, 0, false)]
    [InlineData(PackagingFeeBasis.Percent, 100.5, false)]
    [InlineData(PackagingFeeBasis.Percent, 12.5, true)]
    [InlineData(PackagingFeeBasis.PerUnit, 10000.01, false)]
    [InlineData(PackagingFeeBasis.PerUnit, 2.555, false)]
    [InlineData(PackagingFeeBasis.PerUnit, 25, true)]
    [InlineData(PackagingFeeBasis.None, 99, true)]
    [InlineData((PackagingFeeBasis)7, 5, false)]
    public async Task The_validator_names_a_fee_its_basis_cannot_mean(PackagingFeeBasis basis, decimal value, bool valid)
    {
        var till = await PosTestTill.CreateAsync();

        var result = await new UpdatePosLocationSettingsCommandValidator().ValidateAsync(Save(till, basis, value, null));

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            Assert.All(result.Errors, x => Assert.StartsWith("PackagingFee", x.PropertyName, StringComparison.Ordinal));
        }
    }

    // ---- The product flag ------------------------------------------------------------------------------

    [Fact]
    public async Task A_product_pays_the_fee_by_default_and_an_update_that_does_not_name_the_flag_leaves_it()
    {
        var till = await PosTestTill.CreateAsync();
        Assert.True((await till.Db.Products.SingleAsync(x => x.Id == till.MomoId)).PackagingFeeApplicable);

        var unit = await till.Db.Products.Where(x => x.Id == till.MomoId).Select(x => x.PrimaryUnitId).SingleAsync();
        var created = await new CreateProductCommandHandler(till.Db, till.Seed.NumberGenerator).Handle(
            new CreateProductCommand(
                till.OrganizationId, ProductType.Goods, "Mineral Water", till.Seed.CategoryId, unit, null, true, 20m, 12m,
                VatRate.ThirteenPercentVat, 0, false, PackagingFeeApplicable: false),
            CancellationToken.None);
        Assert.False((await till.Db.Products.SingleAsync(x => x.Id == created.Id)).PackagingFeeApplicable);

        async Task<bool> UpdateAsync(bool? applicable)
        {
            var p = await till.Db.Products.SingleAsync(x => x.Id == created.Id);
            await new UpdateProductCommandHandler(till.Db).Handle(
                new UpdateProductCommand(
                    till.OrganizationId, p.Id, p.Name, p.CategoryId, p.PrimaryUnitId, p.HsCode, p.AvailableForSale,
                    p.SellingPrice, p.PurchasePrice, p.VatRate, p.ReOrderLevel, p.TrackInventory, p.IsActive,
                    PackagingFeeApplicable: applicable),
                CancellationToken.None);
            return (await till.Db.Products.SingleAsync(x => x.Id == created.Id)).PackagingFeeApplicable;
        }

        Assert.False(await UpdateAsync(null));
        Assert.True(await UpdateAsync(true));
        Assert.True(await UpdateAsync(null));
        Assert.False(await UpdateAsync(false));
    }
}
