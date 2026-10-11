using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Exports;
using ErpApp.Application.Exports.Readers;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Exports;

/// <summary>
/// Phase 80 -- the two ledger sheets name the document behind each row (Document No, Reference) beside the id they
/// always printed. A Cash Transfer and a Warehouse Transfer are on no other sheet, so this is where they become
/// readable.
/// </summary>
public sealed class LedgerDocumentNumberExportTests
{
    // Appended after each sheet's original columns.
    private const int LedgerDocumentNo = 7, LedgerReference = 8, LedgerSourceId = 2;
    private const int StockDocumentNo = 11, StockReference = 12, StockSourceId = 9;

    private sealed record Seeded(Guid OrganizationId, Guid OtherTenantsTransferId, IAppDbContext Db);

    private static async Task<Seeded> SeedAsync()
    {
        var db = TestAppDbContext.Create(Guid.NewGuid().ToString());
        var tenant = await ExportTestSeed.SeedAsync(db);
        var other = await ExportTestSeed.SeedAsync(db, "B");
        var org = tenant.OrganizationId;
        var cash = await db.Accounts.SingleAsync(x => x.Code == tenant.AccountCode);
        var sales = await db.Accounts.SingleAsync(x => x.Code == "AC-A-0002");
        var product = await db.Products.SingleAsync(x => x.Code == tenant.ProductCode);
        var store = await db.Warehouses.SingleAsync(x => x.OrganizationId == org);
        var godown = Warehouse.Create(org, "Godown A");
        db.Warehouses.Add(godown);

        var transfer = CashTransfer.Create(org, new DateOnly(2026, 9, 1), "Float to bank", cash.Id);
        transfer.AddLine(sales.Id, 500m);
        transfer.Approve(tenant.AdminUserId, "CT-0001");
        db.CashTransfers.Add(transfer);
        db.GlJournalEntries.Add(GlJournalEntry.Post(
            org, DocumentType.CashTransfer, transfer.Id, [new GlLineInput(sales.Id, 500m, 0m), new GlLineInput(cash.Id, 0m, 500m)]));
        db.GlJournalEntries.Add(GlJournalEntry.Post(
            org, DocumentType.OpeningBalance, Guid.NewGuid(), [new GlLineInput(cash.Id, 50m, 0m), new GlLineInput(sales.Id, 0m, 50m)]));

        var move = WarehouseTransfer.Create(org, store.Id, godown.Id, new DateOnly(2026, 9, 2), "To the godown");
        move.AddLine(product.Id, 4m, null, 1m);
        move.Approve(tenant.AdminUserId, "WT-0001");
        db.WarehouseTransfers.Add(move);
        db.StockMovements.Add(StockMovement.Create(
            org, product.Id, store.Id, StockMovementDirection.Out, 4m, 80m, DocumentType.WarehouseTransfer, move.Id, move.Date));
        db.StockMovements.Add(StockMovement.Create(
            org, product.Id, godown.Id, StockMovementDirection.In, 4m, 80m, DocumentType.WarehouseTransfer, move.Id, move.Date));
        db.StockMovements.Add(StockMovement.Create(
            org, product.Id, store.Id, StockMovementDirection.In, 1m, 80m, DocumentType.OpeningStock, Guid.NewGuid(), new DateOnly(2026, 9, 3)));

        // Another tenant's warehouse transfer, which a movement of this tenant names: it must not resolve.
        // A draft still carries a number ("DRAFT"), so a leak would print one.
        var otherWarehouse = await db.Warehouses.SingleAsync(x => x.OrganizationId == other.OrganizationId);
        var otherGodown = Warehouse.Create(other.OrganizationId, "Godown B");
        db.Warehouses.Add(otherGodown);
        var foreign = WarehouseTransfer.Create(other.OrganizationId, otherWarehouse.Id, otherGodown.Id, new DateOnly(2026, 9, 4), "Not yours");
        db.WarehouseTransfers.Add(foreign);
        db.StockMovements.Add(StockMovement.Create(
            org, product.Id, store.Id, StockMovementDirection.In, 1m, 80m, DocumentType.WarehouseTransfer, foreign.Id, new DateOnly(2026, 9, 4)));

        await db.SaveChangesAsync();
        return new Seeded(org, foreign.Id, db);
    }

    [Fact]
    public async Task Ledger_Transactions_names_a_cash_transfer_and_labels_an_opening_balance()
    {
        var s = await SeedAsync();
        var reader = new LedgerTransactionExportReader(s.Db);
        var rows = (await reader.ReadAsync(s.OrganizationId, 1000, ExportDateRange.Unbounded, CancellationToken.None)).Rows;
        Assert.All(rows, r => Assert.Equal(reader.Headers.Count, r.Length));
        Assert.Equal(("Document No", "Reference"), (reader.Headers[LedgerDocumentNo], reader.Headers[LedgerReference]));

        var transfer = rows.Where(r => Equals(r[1], "CashTransfer")).ToList();
        Assert.Equal(2, transfer.Count);
        Assert.All(transfer, r => Assert.Equal(("CT-0001", "Float to bank"), ((string)r[LedgerDocumentNo]!, (string)r[LedgerReference]!)));

        var opening = rows.Where(r => Equals(r[1], "OpeningBalance")).ToList();
        Assert.All(opening, r => Assert.Equal("Opening Balance", r[LedgerDocumentNo]));

        // The seed's entry names an invoice id that exists nowhere: the id stays, the number is empty.
        var orphan = rows.Where(r => Equals(r[1], "Invoice")).ToList();
        Assert.Equal(2, orphan.Count);
        Assert.All(orphan, r => Assert.NotNull(r[LedgerSourceId]));
        Assert.All(orphan, r => Assert.Null(r[LedgerDocumentNo]));
    }

    [Fact]
    public async Task Stock_Movements_names_a_warehouse_transfer_and_never_another_tenants_document()
    {
        var s = await SeedAsync();
        var reader = new StockMovementExportReader(s.Db);
        var rows = (await reader.ReadAsync(s.OrganizationId, 1000, ExportDateRange.Unbounded, CancellationToken.None)).Rows;
        Assert.All(rows, r => Assert.Equal(reader.Headers.Count, r.Length));

        var move = rows.Where(r => Equals(r[StockDocumentNo], "WT-0001")).ToList();
        Assert.Equal(["Out", "In"], move.Select(r => (string)r[4]!));
        Assert.All(move, r => Assert.Equal("To the godown", r[StockReference]));

        Assert.Equal("Opening Stock", Assert.Single(rows, r => Equals(r[8], "OpeningStock"))[StockDocumentNo]);

        // The tenant filter is on the resolver too: a movement naming another organization's document prints its id
        // and nothing of that document.
        var foreign = Assert.Single(rows, r => Equals(r[StockSourceId], s.OtherTenantsTransferId.ToString()));
        Assert.Null(foreign[StockDocumentNo]);
        Assert.Null(foreign[StockReference]);
    }
}
