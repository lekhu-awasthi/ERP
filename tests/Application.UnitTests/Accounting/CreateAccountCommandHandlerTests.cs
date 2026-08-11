using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

public class CreateAccountCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_account_with_generated_code_and_group_root_type()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var group = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);

        var handler = new CreateAccountCommandHandler(db, new FakeDocumentNumberGenerator());
        var result = await handler.Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", group.Id), CancellationToken.None);

        var account = await db.Accounts.SingleAsync(x => x.Id == result.Id);
        Assert.False(string.IsNullOrWhiteSpace(account.Code));
        Assert.Equal(AccountRootType.Asset, account.RootType);
        Assert.True(account.IsActive);
    }

    [Fact]
    public async Task Handle_throws_not_found_when_group_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var handler = new CreateAccountCommandHandler(db, new FakeDocumentNumberGenerator());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateAccountCommand(Guid.NewGuid(), "Cash in Hand", Guid.NewGuid()), CancellationToken.None));
    }
}
