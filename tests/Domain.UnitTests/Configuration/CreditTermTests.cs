using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

public class CreditTermTests
{
    [Fact]
    public void Create_starts_active_with_given_name_and_due_days()
    {
        var organizationId = Guid.NewGuid();

        var creditTerm = CreditTerm.Create(organizationId, "Net 30", 30);

        Assert.Equal(organizationId, creditTerm.OrganizationId);
        Assert.Equal("Net 30", creditTerm.Name);
        Assert.Equal(30, creditTerm.DueDays);
        Assert.True(creditTerm.IsActive);
    }

    [Fact]
    public void Update_replaces_name_due_days_and_active_flag()
    {
        var creditTerm = CreditTerm.Create(Guid.NewGuid(), "Net 30", 30);

        creditTerm.Update("Net 45", 45, false);

        Assert.Equal("Net 45", creditTerm.Name);
        Assert.Equal(45, creditTerm.DueDays);
        Assert.False(creditTerm.IsActive);
    }
}
