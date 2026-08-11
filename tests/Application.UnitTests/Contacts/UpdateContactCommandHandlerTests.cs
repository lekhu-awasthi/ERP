using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Contacts.Commands.UpdateContact;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Contacts;

namespace ErpApp.Application.UnitTests.Contacts;

public class UpdateContactCommandHandlerTests
{
    [Fact]
    public async Task Handle_updates_editable_fields()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var contact = Contact.Create(
            organizationId, ContactType.Customer, "Acme", "CON-0001", null, null, null, null, null, 0m);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var handler = new UpdateContactCommandHandler(db);
        await handler.Handle(
            new UpdateContactCommand(organizationId, contact.Id, "Acme Traders", "Pokhara", null, null, null, null, 250m),
            CancellationToken.None);

        Assert.Equal("Acme Traders", contact.Name);
        Assert.Equal("Pokhara", contact.Address);
        Assert.Equal(250m, contact.OpeningBalance);
    }

    [Fact]
    public async Task Handle_throws_not_found_when_contact_belongs_to_a_different_organization()
    {
        var db = TestAppDbContext.Create();
        var contact = Contact.Create(
            Guid.NewGuid(), ContactType.Customer, "Acme", "CON-0001", null, null, null, null, null, 0m);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var handler = new UpdateContactCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UpdateContactCommand(Guid.NewGuid(), contact.Id, "Acme", null, null, null, null, null, 0m),
            CancellationToken.None));
    }
}
