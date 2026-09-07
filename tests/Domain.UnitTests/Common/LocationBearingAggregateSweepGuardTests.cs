using System.Reflection;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Common;

/// <summary>
/// Phase 32's sweep guard, in the shape phases 27a and 28 established. The claim being defended is
/// stronger here than phase 28's, because the scope is a <i>runtime</i> setting: an Admin can widen
/// <see cref="LocationScopeMode"/> to AllTransactions at any moment, so every one of
/// <see cref="DocumentMechanisms.LocationBearing"/>'s 17 types must already carry the column. A type
/// added later without one would not fail anything -- it would simply, silently, never record a
/// location, and the Advanced panel's wider option would be a lie for that type alone.
/// </summary>
public class LocationBearingAggregateSweepGuardTests
{
    /// <summary>
    /// The aggregate behind each of <see cref="DocumentMechanisms.LocationBearing"/>'s 17 members.
    /// Kept as an explicit map rather than derived by name so that adding a DocumentType member
    /// fails <see cref="Every_location_bearing_document_type_is_mapped_to_an_aggregate"/> below,
    /// rather than silently mapping to nothing.
    /// </summary>
    private static readonly IReadOnlyDictionary<DocumentType, Type> Aggregates = new Dictionary<DocumentType, Type>
    {
        [DocumentType.Quotation] = typeof(Domain.Sales.Quotation),
        [DocumentType.SalesOrder] = typeof(Domain.Sales.SalesOrder),
        [DocumentType.Invoice] = typeof(Domain.Sales.Invoice),
        [DocumentType.CreditNote] = typeof(Domain.Sales.CreditNote),
        [DocumentType.Payment] = typeof(Domain.Payments.Payment),
        [DocumentType.PurchaseOrder] = typeof(Domain.Purchasing.PurchaseOrder),
        [DocumentType.PurchaseBill] = typeof(Domain.Purchasing.PurchaseBill),
        [DocumentType.Expense] = typeof(Domain.Purchasing.Expense),
        [DocumentType.DebitNote] = typeof(Domain.Purchasing.DebitNote),
        [DocumentType.JournalVoucher] = typeof(Domain.Accounting.JournalVoucher),
        [DocumentType.CashTransfer] = typeof(Domain.Accounting.CashTransfer),
        [DocumentType.WarehouseTransfer] = typeof(Domain.Inventory.WarehouseTransfer),
        [DocumentType.InventoryAdjustment] = typeof(Domain.Inventory.InventoryAdjustment),
        [DocumentType.ProductionOrder] = typeof(Domain.Manufacturing.ProductionOrder),
        [DocumentType.ProductionJournal] = typeof(Domain.Manufacturing.ProductionJournal),
        [DocumentType.OpeningBalance] = typeof(Domain.Accounting.OpeningBalanceLine),
        [DocumentType.OpeningStock] = typeof(Domain.Inventory.OpeningStockLine),
    };

    public static TheoryData<Type> LocationBearingAggregates => [.. Aggregates.Values];

    [Fact]
    public void Every_location_bearing_document_type_is_mapped_to_an_aggregate()
    {
        Assert.Equal(
            DocumentMechanisms.LocationBearing.OrderBy(x => x),
            Aggregates.Keys.OrderBy(x => x));
    }

    [Theory]
    [MemberData(nameof(LocationBearingAggregates))]
    public void Carries_a_nullable_location_id(Type aggregate)
    {
        var location = aggregate.GetProperty("LocationId", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(location);
        Assert.Equal(typeof(Guid?), location.PropertyType);
    }

    [Theory]
    [MemberData(nameof(LocationBearingAggregates))]
    public void Defaults_to_no_location_so_no_backfill_is_needed_in_code(Type aggregate)
    {
        // Constructed through the private parameterless constructor EF itself uses -- the path that
        // proves the property initialisers leave it null, rather than a Create overload that might
        // set it. The *database* backfill is a different question, answered by hand in
        // 20260907161040_Phase32BillingLocations.
        var instance = Activator.CreateInstance(aggregate, nonPublic: true)!;

        Assert.Null(aggregate.GetProperty("LocationId")!.GetValue(instance));
    }

    /// <summary>
    /// The sales-only subset is a strict subset of the all-transactions one, and the reason is worth
    /// pinning: <see cref="DocumentLocationScope"/> selects between the two lists rather than
    /// unioning them, so a member of the narrow list that was missing from the wide one would be a
    /// type that carries a location under the default setting and loses it when an Admin widens the
    /// scope -- backwards, and invisible without this assertion.
    /// </summary>
    [Fact]
    public void The_sales_only_scope_is_a_subset_of_the_all_transactions_scope()
    {
        Assert.All(
            DocumentMechanisms.LocationBearingSalesOnly,
            x => Assert.Contains(x, DocumentMechanisms.LocationBearing));
    }

    [Theory]
    [InlineData(DocumentType.Invoice, true, true)]
    [InlineData(DocumentType.SalesOrder, true, true)]
    [InlineData(DocumentType.CreditNote, true, true)]
    [InlineData(DocumentType.Quotation, false, true)]
    [InlineData(DocumentType.PurchaseBill, false, true)]
    [InlineData(DocumentType.JournalVoucher, false, true)]
    [InlineData(DocumentType.OpeningBalance, false, true)]
    [InlineData(DocumentType.Account, false, false)]
    [InlineData(DocumentType.DataExport, false, false)]
    public void Applies_to_answers_both_modes(DocumentType documentType, bool salesOnly, bool allTransactions)
    {
        Assert.Equal(
            salesOnly,
            DocumentLocationScope.AppliesTo(documentType, LocationScopeMode.SalesTransactionsOnly));
        Assert.Equal(
            allTransactions,
            DocumentLocationScope.AppliesTo(documentType, LocationScopeMode.AllTransactions));
    }
}
