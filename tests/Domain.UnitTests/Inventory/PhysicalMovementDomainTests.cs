using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Inventory;

/// <summary>Phase 58 -- the physical ledger's row, the two documents that write it, and the two
/// order flags their conversions set.</summary>
public class PhysicalMovementDomainTests
{
    private static readonly DateOnly Day = new(2026, 9, 24);

    [Theory]
    [InlineData(DocumentType.Invoice)]
    [InlineData(DocumentType.PurchaseBill)]
    [InlineData(DocumentType.InventoryAdjustment)]
    public void Only_a_delivery_note_or_a_goods_received_note_writes_a_physical_row(DocumentType type)
    {
        // An adjustment reaches the physical ledger through its StockMovement row; writing it here
        // too would count it twice.
        Assert.Throws<InvalidOperationException>(() => PhysicalStockMovement.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), StockMovementDirection.In,
            PrimaryQuantity.AlreadyPrimary(1m), type, Guid.NewGuid(), Day, null));
    }

    [Fact]
    public void A_physical_row_needs_a_positive_quantity()
    {
        Assert.Throws<InvalidOperationException>(() => PhysicalStockMovement.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), StockMovementDirection.In,
            PrimaryQuantity.Zero, DocumentType.GoodsReceivedNote, Guid.NewGuid(), Day, null));
    }

    [Fact]
    public void A_reversal_mirrors_the_row_against_the_same_source()
    {
        var sourceId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var original = PhysicalStockMovement.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), StockMovementDirection.Out,
            PrimaryQuantity.AlreadyPrimary(3m), DocumentType.DeliveryNote, sourceId, Day, locationId);

        var reversal = original.Reverse(Day);

        Assert.NotEqual(original.Id, reversal.Id);
        Assert.Equal(StockMovementDirection.In, reversal.Direction);
        Assert.Equal(3m, reversal.Quantity);
        Assert.Equal(sourceId, reversal.SourceDocumentId);
        Assert.Equal(DocumentType.DeliveryNote, reversal.SourceDocumentType);
        Assert.Equal(original.ProductId, reversal.ProductId);
        Assert.Equal(original.WarehouseId, reversal.WarehouseId);
        Assert.Equal(locationId, reversal.LocationId);
    }

    [Fact]
    public void A_goods_received_note_follows_the_draft_approve_void_lifecycle()
    {
        var note = GoodsReceivedNote.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Day, null, "TRK-1", null, null);

        Assert.Throws<InvalidOperationException>(() => note.Approve(Guid.NewGuid(), "GRN1")); // no lines
        Assert.Throws<InvalidOperationException>(() => note.Void(Guid.NewGuid()));           // not approved

        note.AddLine(Guid.NewGuid(), 10m, 100m, VatRate.ThirteenPercentVat, 0, null, 1m);
        note.Approve(Guid.NewGuid(), "GRN1");

        Assert.Equal("GRN1", note.Code);
        Assert.Throws<InvalidOperationException>(() =>
            note.UpdateHeader(Guid.NewGuid(), Guid.NewGuid(), Day, null, null, 0));        // draft-only

        note.Void(Guid.NewGuid());
        Assert.Equal(GoodsReceivedNoteStatus.Void, note.Status);
    }

    [Fact]
    public void A_goods_received_note_line_prices_like_a_purchase_order_line_and_receives_primary_units()
    {
        var note = GoodsReceivedNote.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Day, null, null, null, null, 10m);
        note.AddLine(Guid.NewGuid(), 2m, 100m, VatRate.ThirteenPercentVat, 5m, Guid.NewGuid(), 12m);

        var line = Assert.Single(note.Lines);
        Assert.Equal(2m * 100m * 0.95m * 0.90m, line.Amount);
        Assert.Equal(line.Amount * 0.13m, line.VatAmount);
        Assert.Equal(24m, line.PrimaryQuantity.Value); // two cartons of twelve
    }

    [Fact]
    public void A_delivery_note_follows_the_draft_approve_void_lifecycle_and_keeps_its_terms_sanitised()
    {
        var note = DeliveryNote.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Day, Day.AddDays(1), null, null, "Gate 2", null, null);
        note.SetTerms("<p>Deliver <script>x</script>before noon</p>");
        note.AddLine(Guid.NewGuid(), 3m, 150m, VatRate.NoVat, 0, null, 1m);
        note.Approve(Guid.NewGuid(), "DO1");

        Assert.Equal(Day.AddDays(1), note.ExpectedDeliveryDate);
        Assert.Equal("Gate 2", note.ShippingAddress);
        Assert.DoesNotContain("script", note.Terms);
        Assert.Throws<InvalidOperationException>(() => note.SetTerms("<p>late</p>")); // draft-only

        note.Void(Guid.NewGuid());
        Assert.Equal(DeliveryNoteStatus.Void, note.Status);
    }

    [Fact]
    public void A_purchase_order_is_received_once_whether_or_not_it_is_billed_and_a_received_one_cannot_be_voided()
    {
        var billedFirst = ApprovedPurchaseOrder();
        billedFirst.MarkConverted();
        billedFirst.MarkReceived(); // a bill first and the goods later is ordinary
        Assert.True(billedFirst.IsReceived);

        var order = ApprovedPurchaseOrder();
        order.MarkReceived();
        Assert.Throws<InvalidOperationException>(order.MarkReceived);
        Assert.Throws<InvalidOperationException>(() => order.Void(Guid.NewGuid()));

        order.MarkConverted(); // and billing stays open after receiving
        Assert.Equal(PurchaseOrderStatus.Converted, order.Status);

        var draft = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Day, null);
        Assert.Throws<InvalidOperationException>(draft.MarkReceived);
    }

    [Fact]
    public void A_sales_order_is_delivered_once_and_a_delivered_one_cannot_be_voided()
    {
        var order = SalesOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Day, null, null);
        Assert.Throws<InvalidOperationException>(order.MarkDelivered); // draft

        order.AddLine(Guid.NewGuid(), 1m, 100m, VatRate.NoVat, 0, null, 1m);
        order.Approve(Guid.NewGuid(), "SO1");
        order.MarkDelivered();

        Assert.True(order.IsDelivered);
        Assert.Throws<InvalidOperationException>(order.MarkDelivered);
        Assert.Throws<InvalidOperationException>(() => order.Void(Guid.NewGuid()));
    }

    private static PurchaseOrder ApprovedPurchaseOrder()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Day, null);
        order.AddLine(Guid.NewGuid(), 1m, 100m, VatRate.NoVat, 0, null, 1m);
        order.Approve(Guid.NewGuid(), "PO1");
        return order;
    }
}
