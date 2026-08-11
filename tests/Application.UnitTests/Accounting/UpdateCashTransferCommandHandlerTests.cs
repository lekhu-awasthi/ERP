using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateCashTransfer;
using ErpApp.Application.Accounting.Commands.UpdateCashTransfer;
using ErpApp.Application.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

public class UpdateCashTransferCommandHandlerTests
{
    [Fact]
    public async Task Handle_replaces_the_entire_line_set()
    {
        var dbName = Guid.NewGuid().ToString();
        var db1 = TestAppDbContext.Create(dbName);
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db1);
        var created = await new CreateCashTransferCommandHandler(db1).Handle(
            new CreateCashTransferCommand(
                organizationId, new DateOnly(2026, 1, 1), null, cashAccountId, [new CashTransferLineInput(salesAccountId, 400m)]),
            CancellationToken.None);

        var db2 = TestAppDbContext.Create(dbName);
        var handler = new UpdateCashTransferCommandHandler(db2);
        await handler.Handle(
            new UpdateCashTransferCommand(
                organizationId, created.Id, new DateOnly(2026, 1, 2), "Revised", cashAccountId,
                [new CashTransferLineInput(salesAccountId, 700m)]),
            CancellationToken.None);

        var db3 = TestAppDbContext.Create(dbName);
        var cashTransfer = await db3.CashTransfers.Include(x => x.Lines).SingleAsync(x => x.Id == created.Id);
        Assert.Single(cashTransfer.Lines);
        Assert.Equal(700m, cashTransfer.Lines[0].Amount);
        Assert.Equal("Revised", cashTransfer.Reference);
    }
}
