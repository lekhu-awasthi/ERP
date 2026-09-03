using ErpApp.Domain.Manufacturing;

namespace ErpApp.Domain.UnitTests.Manufacturing;

/// <summary>
/// Phase 27a -- Production Order is the fourth (and last) type with a custom status.
/// Live-confirmed on the Production Order list grid, where the column is labelled STATUS rather than
/// Sales Order's STAGE but is the same control over the same lookup, and where real rows on the UAT
/// tenant already carry an assigned value alongside their native Approved/Draft state. Production
/// <i>Journal</i> deliberately has none -- its grid is Date/Code/Reference/Product/Quantity only.
/// </summary>
public class ProductionOrderCustomStatusTests
{
    [Fact]
    public void SetCustomStatus_is_allowed_on_a_draft_production_order()
    {
        var order = NewOrder();
        var customStatusId = Guid.NewGuid();

        order.SetCustomStatus(customStatusId);

        Assert.Equal(customStatusId, order.CustomStatusId);
        Assert.Equal(ProductionOrderStatus.Draft, order.Status);
    }

    [Fact]
    public void SetCustomStatus_is_allowed_on_an_approved_production_order()
    {
        var order = NewOrder();
        order.AddRawMaterial(Guid.NewGuid(), 5m);
        order.Approve(Guid.NewGuid(), "PRO0001");
        var customStatusId = Guid.NewGuid();

        order.SetCustomStatus(customStatusId);

        Assert.Equal(customStatusId, order.CustomStatusId);
        Assert.Equal(ProductionOrderStatus.Approved, order.Status);
    }

    [Fact]
    public void SetCustomStatus_null_clears_a_previously_set_status()
    {
        var order = NewOrder();
        order.SetCustomStatus(Guid.NewGuid());

        order.SetCustomStatus(null);

        Assert.Null(order.CustomStatusId);
    }

    private static ProductionOrder NewOrder() =>
        ProductionOrder.Create(
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null, Guid.NewGuid(), 10m, null, null);
}
