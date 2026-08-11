using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateCashTransfer;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

public class CreateCashTransferCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_a_draft_transfer_with_fan_out_lines()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var handler = new CreateCashTransferCommandHandler(db);

        var result = await handler.Handle(
            new CreateCashTransferCommand(
                organizationId, new DateOnly(2026, 1, 1), null, cashAccountId,
                [new CashTransferLineInput(salesAccountId, 400m)]),
            CancellationToken.None);

        Assert.Equal(CashTransfer.DraftCode, result.Code);
        Assert.Equal(CashTransferStatus.Draft, result.Status);

        var cashTransfer = await db.CashTransfers.Include(x => x.Lines).SingleAsync(x => x.Id == result.Id);
        Assert.Single(cashTransfer.Lines);
    }

    [Fact]
    public async Task Handle_throws_not_found_when_from_account_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, _, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var handler = new CreateCashTransferCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateCashTransferCommand(
                organizationId, new DateOnly(2026, 1, 1), null, Guid.NewGuid(), [new CashTransferLineInput(salesAccountId, 400m)]),
            CancellationToken.None));
    }
}
