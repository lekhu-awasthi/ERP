using ErpApp.Application.Sales.Queries.GetCreditNote;
using ErpApp.Application.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Sales;

/// <summary>
/// Phase 89 (phase 86 § 5) -- an ERP return's own page names the warehouse its goods went back into, as its list card has
/// since phase 86 and a till refund's panel since phase 85. The name moved from the till panel to the note's header, so
/// every credit note answers it through the one field, read by <c>CreditNoteReturnWarehouses</c>.
/// </summary>
public sealed class CreditNoteReturnWarehousePageTests
{
    private static readonly DateOnly Day = new(2026, 10, 12);

    [Fact]
    public async Task An_ERP_return_names_its_invoices_warehouse_and_a_standalone_note_names_none()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m);
        var sale = await InventoryReportSeed.SellAsync(db, seed, Day, 4m, 150m);

        var returned = await InventoryReportSeed.CreditNoteAsync(db, seed, Day, 1m, 150m, sale.Id);
        var standalone = await InventoryReportSeed.CreditNoteAsync(db, seed, Day, 1m, 150m);

        async Task<CreditNoteDetailDto> PageAsync(Guid id) => await new GetCreditNoteQueryHandler(db).Handle(
            new GetCreditNoteQuery(seed.OrganizationId, id), CancellationToken.None);

        var warehouse = await db.Warehouses.Where(x => x.Id == seed.WarehouseId).Select(x => x.Name).SingleAsync();
        var page = await PageAsync(returned.Id);
        Assert.Equal(warehouse, page.ReturnWarehouseName);
        Assert.Null(page.PosRefund);
        // A standalone note moved no stock, so it names no warehouse.
        Assert.Null((await PageAsync(standalone.Id)).ReturnWarehouseName);
    }
}
