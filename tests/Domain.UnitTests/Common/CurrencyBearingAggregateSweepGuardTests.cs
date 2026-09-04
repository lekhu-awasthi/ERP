using System.Reflection;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.UnitTests.Common;

/// <summary>
/// Phase 28's sweep guard, in the shape phase 27a established: the claim "every document type that
/// shows Currency + Exchange Rate live has them stored" is exactly the sort of thing that rots
/// silently when a twelfth document type is added, because nothing fails -- the new type simply
/// never offers a currency. So it is asserted, by reflection, against the list the confirm-live
/// pass produced.
/// </summary>
public class CurrencyBearingAggregateSweepGuardTests
{
    /// <summary>
    /// The eleven transactional aggregates whose live form carries "Currency" + "Exchange Rate To
    /// NPR*" (erp-module-scan.md lines 124/160/181/185/471/474, re-confirmed on the Invoice and
    /// Customer Payment forms 2026-09-04), plus OpeningBalanceLine, whose row form carries the same
    /// pair under the labels Currency + Conversion Rate.
    ///
    /// <para>Absent by design: WarehouseTransfer and InventoryAdjustment (no monetary counterparty
    /// -- they move stock at its existing base-currency cost), BillOfMaterials and
    /// ProductionJournal (likewise; a production run consumes and creates FIFO layers, which are a
    /// base-currency store by construction). None of the four shows the pair live.</para>
    /// </summary>
    public static TheoryData<Type> CurrencyBearingAggregates =>
    [
        typeof(Domain.Sales.Quotation), typeof(Domain.Sales.SalesOrder), typeof(Domain.Sales.Invoice),
        typeof(Domain.Sales.CreditNote), typeof(Domain.Purchasing.PurchaseOrder),
        typeof(Domain.Purchasing.PurchaseBill), typeof(Domain.Purchasing.Expense),
        typeof(Domain.Purchasing.DebitNote), typeof(Domain.Accounting.JournalVoucher),
        typeof(Domain.Accounting.CashTransfer), typeof(Domain.Payments.Payment),
        typeof(Domain.Accounting.OpeningBalanceLine),
    ];

    [Theory]
    [MemberData(nameof(CurrencyBearingAggregates))]
    public void Carries_a_currency_code_and_an_exchange_rate(Type aggregate)
    {
        var code = aggregate.GetProperty("CurrencyCode", BindingFlags.Public | BindingFlags.Instance);
        var rate = aggregate.GetProperty("ExchangeRate", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(code);
        Assert.Equal(typeof(string), code.PropertyType);
        Assert.NotNull(rate);
        Assert.Equal(typeof(decimal), rate.PropertyType);
    }

    [Theory]
    [MemberData(nameof(CurrencyBearingAggregates))]
    public void Defaults_to_the_base_currency_at_rate_one_so_no_backfill_is_needed(Type aggregate)
    {
        // Constructed through the private parameterless constructor EF itself uses, which is the
        // path that proves the *property initialisers* carry the default -- not a Create overload
        // that might set them explicitly.
        var instance = Activator.CreateInstance(aggregate, nonPublic: true)!;

        Assert.Equal(CurrencyCatalog.BaseCode, aggregate.GetProperty("CurrencyCode")!.GetValue(instance));
        Assert.Equal(ExchangeRates.BaseRate, aggregate.GetProperty("ExchangeRate")!.GetValue(instance));
    }
}
