using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveCashTransfer;
using ErpApp.Application.Accounting.Commands.CreateCashTransfer;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

public class ApproveCashTransferCommandHandlerTests
{
    [Fact]
    public async Task Handle_posts_a_balanced_gl_entry_for_a_fan_out_transfer()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var created = await new CreateCashTransferCommandHandler(db).Handle(
            new CreateCashTransferCommand(
                organizationId, new DateOnly(2026, 1, 1), null, cashAccountId,
                [new CashTransferLineInput(salesAccountId, 400m)]),
            CancellationToken.None);
        var handler = new ApproveCashTransferCommandHandler(
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new CashTransferPostingRule());

        var result = await handler.Handle(new ApproveCashTransferCommand(organizationId, created.Id), CancellationToken.None);

        Assert.Equal(CashTransferStatus.Approved, result.Status);

        var glEntry = await db.GlJournalEntries.Include(x => x.Lines).SingleAsync(
            x => x.SourceDocumentType == DocumentType.CashTransfer && x.SourceDocumentId == created.Id);
        Assert.Equal(2, glEntry.Lines.Count);
        Assert.Equal(400m, glEntry.Lines.Sum(x => x.Debit));
        Assert.Equal(glEntry.Lines.Sum(x => x.Debit), glEntry.Lines.Sum(x => x.Credit));
    }

    [Fact]
    public async Task Handle_throws_conflict_when_no_lines()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, _) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var created = await new CreateCashTransferCommandHandler(db).Handle(
            new CreateCashTransferCommand(organizationId, new DateOnly(2026, 1, 1), null, cashAccountId, []),
            CancellationToken.None);
        var handler = new ApproveCashTransferCommandHandler(
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new CashTransferPostingRule());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new ApproveCashTransferCommand(organizationId, created.Id), CancellationToken.None));
    }
}
