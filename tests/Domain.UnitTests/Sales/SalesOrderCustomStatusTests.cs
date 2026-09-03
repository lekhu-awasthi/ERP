using ErpApp.Domain.Catalog;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Sales;

/// <summary>
/// Phase 27a -- Sales Order joins Quotation and Purchase Order in carrying a custom status.
/// Live-confirmed: the Sales Orders list grid has a STAGE column with a per-row "Select Status"
/// popover offering the tenant's Sales Order pipeline, on Draft and Approved rows alike. Same shape
/// and same reasoning as PurchaseOrderTests; the point of restating it per type is that "orthogonal
/// to the native lifecycle" is an invariant per aggregate, not a property of the mechanism.
/// </summary>
public class SalesOrderCustomStatusTests
{
    [Fact]
    public void SetCustomStatus_is_allowed_on_a_draft_sales_order()
    {
        var salesOrder = SalesOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Today(), null, null);
        var customStatusId = Guid.NewGuid();

        salesOrder.SetCustomStatus(customStatusId);

        Assert.Equal(customStatusId, salesOrder.CustomStatusId);
        Assert.Equal(SalesOrderStatus.Draft, salesOrder.Status);
    }

    [Fact]
    public void SetCustomStatus_is_allowed_on_an_approved_sales_order()
    {
        var salesOrder = SalesOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Today(), null, null);
        salesOrder.AddLine(Guid.NewGuid(), 1m, 100m, VatRate.NoVat, 0);
        salesOrder.Approve(Guid.NewGuid(), "SO0001");
        var customStatusId = Guid.NewGuid();

        salesOrder.SetCustomStatus(customStatusId);

        Assert.Equal(customStatusId, salesOrder.CustomStatusId);
        Assert.Equal(SalesOrderStatus.Approved, salesOrder.Status);
    }

    [Fact]
    public void SetCustomStatus_null_clears_a_previously_set_status()
    {
        var salesOrder = SalesOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Today(), null, null);
        salesOrder.SetCustomStatus(Guid.NewGuid());

        salesOrder.SetCustomStatus(null);

        Assert.Null(salesOrder.CustomStatusId);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
