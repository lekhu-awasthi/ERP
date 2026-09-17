using System.Reflection;
using ErpApp.Application.Inventory;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Sales;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;

namespace ErpApp.Application.UnitTests.Inventory;

/// <summary>
/// Phase 52 — the unit sweep, asserted in both directions.
///
/// <para><b>Why a guard, and why this shape.</b> Decision E made the <i>write</i> half of this
/// phase compiler-enforced: <c>PrimaryQuantity</c> is a distinct type, so a ledger call passing a
/// raw line quantity cannot build. That is the half phase 51 got for free on <c>ConsumeAsync</c>
/// and did not get on <c>IncrementAsync</c>, and it is why this phase's equivalent bug was caught
/// at the keyboard rather than three steps later in another subsystem.</para>
///
/// <para><b>The read half has no such enforcement</b>, and that is what these tests are for. Adding
/// a member to a record is additive: a detail DTO, a line input or a conversion template that
/// silently drops the unit compiles, passes every handler test, and leaves the form unable to show
/// what was saved. Phase 35a found 14 of 15 detail DTOs and all 5 conversion templates dropping
/// <c>LocationId</c> exactly that way, and phase 51 was bitten four times in the same place.</para>
///
/// <para>The sets below are written out rather than derived from a marker interface, for phase 50's
/// reason: a guard whose predicate names a <i>type</i> stops covering anything that predates the
/// type, and a guard asserting a rule's consequences is not a guard on the rule. These are the
/// eight line types the 2026-09-17 live read found carrying <c>measurement_unit_id</c> — including
/// Warehouse Transfer, which carries no price and which a list sampled from the sales and purchase
/// forms would have missed (phase 30).</para>
/// </summary>
public class UnitSweepGuardTests
{
    /// <summary>The eight aggregates whose lines carry a unit. Quotation, Sales Order, Invoice,
    /// Credit Note, Purchase Order, Purchase Bill, Debit Note — and Warehouse Transfer.</summary>
    public static TheoryData<Type> LineEntities =>
    [
        typeof(QuotationLine), typeof(SalesOrderLine), typeof(InvoiceLine), typeof(CreditNoteLine),
        typeof(PurchaseOrderLine), typeof(PurchaseBillLine), typeof(DebitNoteLine),
        typeof(WarehouseTransferLine),
    ];

    /// <summary>The request records a client sends one of those lines on.</summary>
    public static TheoryData<Type> LineInputs =>
    [
        typeof(QuotationLineInput), typeof(SalesOrderLineInput), typeof(InvoiceLineInput),
        typeof(CreditNoteLineInput), typeof(PurchaseOrderLineInput), typeof(PurchaseBillLineInput),
        typeof(DebitNoteLineInput), typeof(WarehouseTransferLineInput),
    ];

    /// <summary>
    /// The line types that carry <b>no</b> unit, restated here with the reason, so that a future
    /// phase adding one to them has to delete a line from this list and think about it rather than
    /// discovering the gap from a report. Opening Stock, Inventory Adjustment and the Production
    /// Journal's three collections were all <b>unread</b> on 2026-09-17 — that tenant had none of
    /// those documents, so the list endpoint 404'd on its first row — which makes this a deferral
    /// on the phase-8f rule, not a finding.
    /// </summary>
    public static TheoryData<string, string> UnitlessLineTypes => new()
    {
        { "OpeningStockLine", "unread: the reference tenant had no opening stock to read" },
        { "InventoryAdjustmentLine", "unread: the reference tenant had no inventory adjustments" },
        { "ProductionJournalRawMaterialLine", "unread: the reference tenant had no production journals" },
    };

    [Theory]
    [MemberData(nameof(LineEntities))]
    public void Every_line_entity_stores_the_unit_and_the_factor(Type lineType)
    {
        Assert.NotNull(lineType.GetProperty("UnitId"));
        Assert.Equal(typeof(Guid?), lineType.GetProperty("UnitId")!.PropertyType);

        Assert.NotNull(lineType.GetProperty("ConversionFactor"));
        Assert.Equal(typeof(decimal), lineType.GetProperty("ConversionFactor")!.PropertyType);
    }

    [Theory]
    [MemberData(nameof(LineEntities))]
    public void Every_line_entity_derives_its_primary_quantity_and_never_stores_it(Type lineType)
    {
        var primary = lineType.GetProperty("PrimaryQuantity");

        Assert.NotNull(primary);
        Assert.Equal(typeof(PrimaryQuantity), primary!.PropertyType);

        // Get-only: a settable PrimaryQuantity would be a second quantity able to contradict
        // Quantity and ConversionFactor, which is the shape phase 51 refused for ProductBatch and
        // phase 37 found drifting between three views.
        Assert.Null(primary.SetMethod);
    }

    [Theory]
    [MemberData(nameof(LineEntities))]
    public void No_line_entity_stores_a_second_quantity(Type lineType)
    {
        // The vendor stores the converted quantity (`primary_quantity`); this codebase stores the
        // factor and derives it. Either is defensible, but holding both is not — so assert the
        // column does not appear under any of the names a future phase might reach for.
        foreach (var name in new[] { "PrimaryQuantityValue", "ConvertedQuantity", "BaseQuantity" })
        {
            Assert.Null(lineType.GetProperty(name));
        }
    }

    [Theory]
    [MemberData(nameof(LineInputs))]
    public void Every_line_input_accepts_a_unit_and_never_a_factor(Type inputType)
    {
        var unit = inputType.GetProperty("UnitId");

        Assert.NotNull(unit);
        Assert.Equal(typeof(Guid?), unit!.PropertyType);

        // The factor is resolved from the catalogue by DocumentLineUnitResolver and frozen on the
        // line. A client that could send its own would be able to state a conversion the product
        // does not have, which is the whole guarantee this phase rests on.
        Assert.Null(inputType.GetProperty("ConversionFactor"));
    }

    [Theory]
    [MemberData(nameof(LineInputs))]
    public void Every_line_input_defaults_its_unit_to_the_primary(Type inputType)
    {
        // Null means "the product's own primary unit", so a caller written before this phase — and
        // the Api endpoints, which bind these records directly — behaves exactly as it did.
        var ctor = inputType.GetConstructors().Single();
        var unit = ctor.GetParameters().Single(p => p.Name == "UnitId");

        Assert.True(unit.HasDefaultValue);
        Assert.Null(unit.DefaultValue);
    }

    [Theory]
    [MemberData(nameof(UnitlessLineTypes))]
    public void The_deferred_line_types_still_carry_no_unit(string typeName, string reason)
    {
        // Asserted in the *negative* direction on purpose (phase 46: an exclusion has to say what it
        // is excluding and why, and be asserted to still exist). If a later phase gives one of these
        // a unit, this test fails and the reason above has to be revisited rather than silently
        // outliving the fact it describes.
        var type = typeof(StockLedgerEntry).Assembly.GetTypes()
            .SingleOrDefault(t => t.Name == typeName);

        Assert.True(type is not null, $"{typeName} no longer exists; the deferral ({reason}) needs re-reading.");
        Assert.Null(type!.GetProperty("UnitId"));
    }

    [Fact]
    public void The_stock_ledger_accepts_only_a_primary_quantity()
    {
        // The load-bearing assertion of Decision E. If this ever goes back to `decimal`, every call
        // site silently compiles again and the phase-51 bug becomes reachable from any new handler.
        var ledger = typeof(Application.Inventory.Stock.IStockLedgerService);

        foreach (var name in new[] { "IncrementAsync", "ConsumeAsync", "PreviewConsumptionCostAsync" })
        {
            var method = ledger.GetMethod(name);
            Assert.True(method is not null, $"{name} is gone; Decision E's enforcement went with it.");

            var quantity = method!.GetParameters()
                .SingleOrDefault(p => p.ParameterType == typeof(PrimaryQuantity));

            Assert.True(
                quantity is not null,
                $"{name} no longer takes a PrimaryQuantity. A raw decimal here is how phase 51 shipped "
                + "an un-batched layer past 1,900 green tests.");
        }
    }

    [Fact]
    public void PrimaryQuantity_cannot_be_built_from_a_bare_decimal()
    {
        // No implicit conversion, no public constructor: the only ways in are the two named
        // factories, each of which forces the caller to say which kind of quantity it holds.
        Assert.Empty(typeof(PrimaryQuantity).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var implicitOps = typeof(PrimaryQuantity)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "op_Implicit");

        Assert.Empty(implicitOps);
    }
}
