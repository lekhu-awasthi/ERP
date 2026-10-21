using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phases 86-87 -- a sale records every account its approval posts to (Receivable, VAT, COGS and Inventory, the two fees,
/// the round-off since phase 96, and each line's Sales account), so whatever reverses or settles it later can use the same
/// ones. Recorded once, at the approval, and only for a leg the sale posts.
/// </summary>
public sealed class SaleFeeAccountTests
{
    private static readonly PackagingFee TwentyABox = PackagingFee.Create(PackagingFeeBasis.PerUnit, 20m);

    private static Invoice Sale(PosTab tab, decimal serviceChargeRate, PackagingFee fee, VatRate? vat = null, int lines = 1)
    {
        var sale = Invoice.CreatePosSale(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 12), Guid.NewGuid(), Guid.NewGuid(), tab);
        for (var i = 0; i < lines; i++)
        {
            sale.AddPosLine(
                Guid.NewGuid(), 1m, 200m, vat ?? VatRate.ThirteenPercentVat, 0m, null, 1m, null, serviceChargeRate, fee);
        }

        return sale;
    }

    private static InvoicePostingAccounts Accounts(
        int lines, Guid? serviceCharge = null, Guid? packaging = null, Guid? receivable = null, Guid? vat = null,
        Guid? rounding = null) => new(
        receivable ?? Guid.NewGuid(), vat ?? Guid.NewGuid(), null, null, serviceCharge, packaging, rounding,
        [.. Enumerable.Range(0, lines).Select(_ => Guid.NewGuid())]);

    [Fact]
    public void A_sale_records_every_account_its_approval_posts_to()
    {
        var serviceCharge = Guid.NewGuid();
        var packaging = Guid.NewGuid();
        var receivable = Guid.NewGuid();
        var vat = Guid.NewGuid();
        var sale = Sale(PosTab.TakeAway, 10m, TwentyABox, lines: 2);
        var accounts = Accounts(2, serviceCharge, packaging, receivable, vat);

        sale.RecordPostingAccounts(accounts);
        sale.Approve(Guid.NewGuid(), "0001");

        Assert.Equal((serviceCharge, packaging), (sale.ServiceChargeAccountId, sale.PackagingFeeAccountId));
        Assert.Equal((receivable, vat), (sale.ReceivableAccountId, sale.VatPayableAccountId));
        Assert.Equal(accounts.LineSalesAccountIds, [.. sale.Lines.Select(x => x.SalesAccountId!.Value)]);
    }

    [Fact]
    public void A_leg_the_sale_does_not_post_records_no_account()
    {
        var sale = Sale(PosTab.Retail, 0m, PackagingFee.None, VatRate.NoVat);

        sale.RecordPostingAccounts(Accounts(1, Guid.NewGuid(), Guid.NewGuid()));

        Assert.Null(sale.ServiceChargeAccountId);
        Assert.Null(sale.PackagingFeeAccountId);
        Assert.Null(sale.VatPayableAccountId);
        Assert.NotNull(sale.ReceivableAccountId);
    }

    [Fact]
    public void A_rounded_sale_records_its_rounding_account_and_an_unrounded_one_none()
    {
        var rounding = Guid.NewGuid();
        var rounded = Sale(PosTab.Retail, 0m, PackagingFee.None);
        rounded.SetPosRoundOff(0.20m);
        rounded.RecordPostingAccounts(Accounts(1, rounding: rounding));
        Assert.Equal(rounding, rounded.RoundingAccountId);

        var whole = Sale(PosTab.Retail, 0m, PackagingFee.None);
        whole.RecordPostingAccounts(Accounts(1, rounding: Guid.NewGuid()));
        Assert.Null(whole.RoundingAccountId);
    }

    [Fact]
    public void The_accounts_are_recorded_at_the_approval_never_after_it_and_one_per_line()
    {
        var sale = Sale(PosTab.Retail, 10m, PackagingFee.None, lines: 2);
        Assert.Throws<InvalidOperationException>(() => sale.RecordPostingAccounts(Accounts(1)));

        var accounts = Accounts(2, Guid.NewGuid());
        sale.RecordPostingAccounts(accounts);
        sale.Approve(Guid.NewGuid(), "0001");

        Assert.Throws<InvalidOperationException>(() => sale.RecordPostingAccounts(Accounts(2, Guid.NewGuid())));
        Assert.Equal(accounts.ServiceChargeAccountId, sale.ServiceChargeAccountId);
    }
}
