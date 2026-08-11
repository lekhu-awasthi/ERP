using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Contacts.Commands.CreateContactGroup;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Contacts;

public class CreateContactGroupCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_root_group()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreateContactGroupCommandHandler(db);

        var result = await handler.Handle(
            new CreateContactGroupCommand(organizationId, "Wholesale", null), CancellationToken.None);

        var group = await db.ContactGroups.SingleAsync(x => x.Id == result.Id);
        Assert.Null(group.ParentGroupId);
    }

    [Fact]
    public async Task Handle_throws_conflict_when_name_already_used_in_organization()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.ContactGroups.Add(ContactGroup.Create(organizationId, "Wholesale", null));
        await db.SaveChangesAsync();

        var handler = new CreateContactGroupCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateContactGroupCommand(organizationId, "Wholesale", null), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_not_found_when_parent_group_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var handler = new CreateContactGroupCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateContactGroupCommand(Guid.NewGuid(), "Wholesale", Guid.NewGuid()), CancellationToken.None));
    }
}
