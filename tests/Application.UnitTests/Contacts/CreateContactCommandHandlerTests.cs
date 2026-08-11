using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Contacts;

public class CreateContactCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_contact_with_a_generated_code()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreateContactCommandHandler(db, new FakeDocumentNumberGenerator());

        var result = await handler.Handle(
            new CreateContactCommand(
                organizationId, ContactType.Customer, "Acme Traders", "Kathmandu", null, null, null, null, 0m),
            CancellationToken.None);

        var contact = await db.Contacts.SingleAsync(x => x.Id == result.Id);
        Assert.Equal(organizationId, contact.OrganizationId);
        Assert.Equal(ContactType.Customer, contact.Type);
        Assert.False(string.IsNullOrWhiteSpace(contact.Code));
        Assert.True(contact.IsActive);
    }

    [Fact]
    public async Task Handle_throws_not_found_when_group_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var handler = new CreateContactCommandHandler(db, new FakeDocumentNumberGenerator());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateContactCommand(
                Guid.NewGuid(), ContactType.Supplier, "Acme", null, null, null, null, Guid.NewGuid(), 0m),
            CancellationToken.None));
    }
}
