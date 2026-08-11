using ErpApp.Domain.Contacts;

namespace ErpApp.Domain.UnitTests.Contacts;

public class ContactTests
{
    [Fact]
    public void Create_starts_active_with_given_fields()
    {
        var organizationId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var contact = Contact.Create(
            organizationId, ContactType.Customer, "Acme Traders", "CON-0001",
            "Kathmandu", "PAN123", "9800000000", "acme@example.com", groupId, 1000m);

        Assert.Equal(organizationId, contact.OrganizationId);
        Assert.Equal(ContactType.Customer, contact.Type);
        Assert.Equal("Acme Traders", contact.Name);
        Assert.Equal("CON-0001", contact.Code);
        Assert.Equal(groupId, contact.GroupId);
        Assert.Equal(1000m, contact.OpeningBalance);
        Assert.True(contact.IsActive);
    }

    [Fact]
    public void Update_replaces_editable_fields_but_not_type_or_code()
    {
        var contact = Contact.Create(
            Guid.NewGuid(), ContactType.Supplier, "Acme Traders", "CON-0001", null, null, null, null, null, 0m);

        contact.Update("Acme Traders Pvt Ltd", "Pokhara", "PAN456", "9811111111", "new@example.com", null, 500m);

        Assert.Equal("Acme Traders Pvt Ltd", contact.Name);
        Assert.Equal("Pokhara", contact.Address);
        Assert.Equal(500m, contact.OpeningBalance);
        Assert.Equal(ContactType.Supplier, contact.Type);
        Assert.Equal("CON-0001", contact.Code);
    }

    [Fact]
    public void Deactivate_sets_is_active_false()
    {
        var contact = Contact.Create(
            Guid.NewGuid(), ContactType.Lead, "Someone", "CON-0002", null, null, null, null, null, 0m);

        contact.Deactivate();

        Assert.False(contact.IsActive);
    }
}
