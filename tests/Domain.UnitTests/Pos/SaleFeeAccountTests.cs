using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 86 -- a sale records the accounts its approval credits with the service charge and packaging fee, so a refund
/// can reverse into them after the location's setting has changed. Recorded once, at the approval, and only for a figure
/// the sale carries.
/// </summary>
public sealed class SaleFeeAccountTests
{
    private static readonly PackagingFee TwentyABox = PackagingFee.Create(PackagingFeeBasis.PerUnit, 20m);

    private static Invoice Sale(PosTab tab, decimal serviceChargeRate, PackagingFee fee)
    {
        var sale = Invoice.CreatePosSale(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 12), Guid.NewGuid(), Guid.NewGuid(), tab);
        sale.AddPosLine(Guid.NewGuid(), 1m, 200m, VatRate.ThirteenPercentVat, 0m, null, 1m, null, serviceChargeRate, fee);
        return sale;
    }

    [Fact]
    public void A_sale_records_the_accounts_it_credits_with_its_service_charge_and_packaging_fee()
    {
        var serviceCharge = Guid.NewGuid();
        var packaging = Guid.NewGuid();
        var sale = Sale(PosTab.TakeAway, 10m, TwentyABox);

        sale.RecordFeeAccounts(serviceCharge, packaging);
        sale.Approve(Guid.NewGuid(), "0001");

        Assert.Equal(serviceCharge, sale.ServiceChargeAccountId);
        Assert.Equal(packaging, sale.PackagingFeeAccountId);
    }

    [Fact]
    public void A_figure_the_sale_does_not_carry_records_no_account()
    {
        var sale = Sale(PosTab.Retail, 0m, PackagingFee.None);

        sale.RecordFeeAccounts(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(sale.ServiceChargeAccountId);
        Assert.Null(sale.PackagingFeeAccountId);
    }

    [Fact]
    public void The_accounts_are_recorded_at_the_approval_and_never_after_it()
    {
        var serviceCharge = Guid.NewGuid();
        var sale = Sale(PosTab.Retail, 10m, PackagingFee.None);
        sale.RecordFeeAccounts(serviceCharge, null);
        sale.Approve(Guid.NewGuid(), "0001");

        Assert.Throws<InvalidOperationException>(() => sale.RecordFeeAccounts(Guid.NewGuid(), null));
        Assert.Equal(serviceCharge, sale.ServiceChargeAccountId);
    }
}
