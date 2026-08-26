using ErpApp.Domain.Catalog;
using ErpApp.Domain.Purchasing;

namespace ErpApp.Domain.UnitTests.Purchasing;

/// <summary>Phase 20b -- mirror of QuotationTests.SetCustomStatus (identical shape/reasoning).</summary>
public class PurchaseOrderTests
{
    [Fact]
    public void SetCustomStatus_is_allowed_on_a_draft_purchase_order()
    {
        var purchaseOrder = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Today(), null);
        var customStatusId = Guid.NewGuid();

        purchaseOrder.SetCustomStatus(customStatusId);

        Assert.Equal(customStatusId, purchaseOrder.CustomStatusId);
        Assert.Equal(PurchaseOrderStatus.Draft, purchaseOrder.Status);
    }

    [Fact]
    public void SetCustomStatus_is_allowed_on_an_approved_purchase_order()
    {
        var purchaseOrder = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Today(), null);
        purchaseOrder.AddLine(Guid.NewGuid(), 1m, 100m, VatRate.NoVat, 0);
        purchaseOrder.Approve(Guid.NewGuid(), "PO0001");
        var customStatusId = Guid.NewGuid();

        purchaseOrder.SetCustomStatus(customStatusId);

        Assert.Equal(customStatusId, purchaseOrder.CustomStatusId);
        Assert.Equal(PurchaseOrderStatus.Approved, purchaseOrder.Status);
    }

    [Fact]
    public void SetCustomStatus_null_clears_a_previously_set_status()
    {
        var purchaseOrder = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Today(), null);
        purchaseOrder.SetCustomStatus(Guid.NewGuid());

        purchaseOrder.SetCustomStatus(null);

        Assert.Null(purchaseOrder.CustomStatusId);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
