using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

public class CreateJournalVoucherCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_a_draft_voucher_with_lines()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var handler = new CreateJournalVoucherCommandHandler(db);

        var result = await handler.Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), "Cash sale",
                [new JournalVoucherLineInput(cashAccountId, 1000m, 0m), new JournalVoucherLineInput(salesAccountId, 0m, 1000m)]),
            CancellationToken.None);

        Assert.Equal(JournalVoucher.DraftCode, result.Code);
        Assert.Equal(JournalVoucherStatus.Draft, result.Status);

        var journalVoucher = await db.JournalVouchers.Include(x => x.Lines).SingleAsync(x => x.Id == result.Id);
        Assert.Equal(2, journalVoucher.Lines.Count);
    }

    [Fact]
    public async Task Handle_throws_not_found_when_an_account_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, _) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var handler = new CreateJournalVoucherCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), null,
                [new JournalVoucherLineInput(cashAccountId, 1000m, 0m), new JournalVoucherLineInput(Guid.NewGuid(), 0m, 1000m)]),
            CancellationToken.None));
    }
}
