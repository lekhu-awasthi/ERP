using ErpApp.Domain.Contacts;

namespace ErpApp.Domain.UnitTests.Contacts;

public class ContactPersonnelTests
{
    [Fact]
    public void Create_sets_given_fields()
    {
        var organizationId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var personnel = ContactPersonnel.Create(
            organizationId, contactId, "Ram Sharma", "Kathmandu", "P001", "9800000000", groupId,
            "ram@example.com", "Manager");

        Assert.Equal(organizationId, personnel.OrganizationId);
        Assert.Equal(contactId, personnel.ContactId);
        Assert.Equal("Ram Sharma", personnel.Name);
        Assert.Equal("Kathmandu", personnel.Address);
        Assert.Equal("P001", personnel.Code);
        Assert.Equal("9800000000", personnel.Phone);
        Assert.Equal(groupId, personnel.GroupId);
        Assert.Equal("ram@example.com", personnel.Email);
        Assert.Equal("Manager", personnel.OrganizationTitle);
    }

    [Fact]
    public void Create_allows_all_optional_fields_to_be_null()
    {
        var personnel = ContactPersonnel.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Ram Sharma", null, null, null, null, null, null);

        Assert.Null(personnel.Address);
        Assert.Null(personnel.Code);
        Assert.Null(personnel.Phone);
        Assert.Null(personnel.GroupId);
        Assert.Null(personnel.Email);
        Assert.Null(personnel.OrganizationTitle);
    }

    [Fact]
    public void Update_replaces_every_field()
    {
        var personnel = ContactPersonnel.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Ram Sharma", "Kathmandu", "P001", "9800000000", null,
            "ram@example.com", "Manager");
        var newGroupId = Guid.NewGuid();

        personnel.Update("Shyam Thapa", "Pokhara", "P002", "9811111111", newGroupId, "shyam@example.com", "Director");

        Assert.Equal("Shyam Thapa", personnel.Name);
        Assert.Equal("Pokhara", personnel.Address);
        Assert.Equal("P002", personnel.Code);
        Assert.Equal("9811111111", personnel.Phone);
        Assert.Equal(newGroupId, personnel.GroupId);
        Assert.Equal("shyam@example.com", personnel.Email);
        Assert.Equal("Director", personnel.OrganizationTitle);
    }
}
