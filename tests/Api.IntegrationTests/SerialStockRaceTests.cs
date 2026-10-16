using ErpApp.Application.Common.Exceptions;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Phase 93 -- two documents putting one serial into stock at the same moment answer a 409, not a 500. The ledger's
/// own check (phase 92's <c>SerialStock</c>) reads and then writes, so two approvals racing for one unit both read it
/// absent; the second save meets phase 51's filtered unique index, which <c>AppDbContext.SaveChangesAsync</c> now turns
/// into a <see cref="ConflictException"/>. SQL Server only: InMemory enforces no unique index (phase 20e), so the race
/// is staged here as it happens -- two contexts that each read nothing and each write the unit.
///
/// <para>The ledger table's foreign keys are switched off in this throwaway database, so a layer needs no product,
/// warehouse or organization row behind it: the subject is the index, not the rows it points at.</para>
/// </summary>
public sealed class SerialStockRaceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder().Build();

    private AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_sqlContainer.GetConnectionString()).Options);

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE inventory.StockLedgerEntries NOCHECK CONSTRAINT ALL");
    }

    public Task DisposeAsync() => _sqlContainer.DisposeAsync().AsTask();

    private static StockLedgerEntry Unit(Guid organizationId, Guid productId, string serialNo) =>
        StockLedgerEntry.Create(
            organizationId, productId, Guid.NewGuid(), 1m, 8000m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 10, 15), serialNo: serialNo);

    [Fact]
    public async Task Two_documents_receiving_one_serial_at_once_leave_one_unit_and_answer_the_second_a_409()
    {
        var organizationId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await using var first = CreateContext();
        await using var second = CreateContext();

        // Both read the unit absent, as SerialStock does before it writes.
        Assert.False(await first.StockLedgerEntries.AnyAsync(x => x.ProductId == productId && x.SerialNo == "IMEI-93"));
        Assert.False(await second.StockLedgerEntries.AnyAsync(x => x.ProductId == productId && x.SerialNo == "IMEI-93"));

        first.StockLedgerEntries.Add(Unit(organizationId, productId, "IMEI-93"));
        second.StockLedgerEntries.Add(Unit(organizationId, productId, "imei-93"));

        await first.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal(AppDbContext.SerialInStockRaceMessage, conflict.Message);

        await using var read = CreateContext();
        Assert.Equal(1, await read.StockLedgerEntries.CountAsync(x => x.ProductId == productId));
    }

    [Fact]
    public async Task Another_unique_index_still_surfaces_as_the_database_error_it_is()
    {
        // The mapping names its index: a duplicate anywhere else is not a serial in stock twice and must not read as one.
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE catalog.ProductBatches NOCHECK CONSTRAINT ALL");
        var organizationId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        db.ProductBatches.Add(Domain.Catalog.ProductBatch.Create(organizationId, productId, "B93", null, null));
        db.ProductBatches.Add(Domain.Catalog.ProductBatch.Create(organizationId, productId, "B93", null, null));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_serial_back_in_stock_after_it_left_is_not_a_conflict()
    {
        // The index is filtered on QuantityRemaining > 0: a unit sold and bought back is a second layer, legitimately.
        var organizationId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var db = CreateContext();

        var sold = Unit(organizationId, productId, "IMEI-94");
        db.StockLedgerEntries.Add(sold);
        await db.SaveChangesAsync();
        sold.Consume(1m);
        db.StockLedgerEntries.Add(Unit(organizationId, productId, "IMEI-94"));

        await db.SaveChangesAsync();
    }
}
