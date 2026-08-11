using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Contacts.Commands.DeactivateContact;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Contacts;

namespace ErpApp.Application.UnitTests.Contacts;

public class DeactivateContactCommandHandlerTests
{
    [Fact]
    public async Task Handle_sets_is_active_false()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var contact = Contact.Create(
            organizationId, ContactType.Lead, "Someone", "CON-0001", null, null, null, null, null, 0m);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var handler = new DeactivateContactCommandHandler(db);
        await handler.Handle(new DeactivateContactCommand(organizationId, contact.Id), CancellationToken.None);

        Assert.False(contact.IsActive);
    }

    [Fact]
    public async Task Handle_throws_not_found_for_unknown_id()
    {
        var db = TestAppDbContext.Create();
        var handler = new DeactivateContactCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new DeactivateContactCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
    }
}
