using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

public class CreateAccountGroupCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_a_root_group()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreateAccountGroupCommandHandler(db);

        var result = await handler.Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);

        var group = await db.AccountGroups.SingleAsync(x => x.Id == result.Id);
        Assert.Equal(AccountRootType.Asset, group.RootType);
        Assert.Null(group.ParentGroupId);
    }

    [Fact]
    public async Task Handle_throws_conflict_when_name_already_exists()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreateAccountGroupCommandHandler(db);
        await handler.Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_conflict_when_root_type_differs_from_parent()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreateAccountGroupCommandHandler(db);
        var parent = await handler.Handle(
            new CreateAccountGroupCommand(organizationId, "Assets", AccountRootType.Asset, null), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateAccountGroupCommand(organizationId, "Payables", AccountRootType.Liability, parent.Id), CancellationToken.None));
    }
}
