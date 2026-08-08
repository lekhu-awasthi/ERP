using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Tenancy;

public class OrganizationTests
{
    [Fact]
    public void Create_lowercases_workspace_name()
    {
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "Acme-Traders", null, null, null, null, Guid.NewGuid());

        Assert.Equal("acme-traders", organization.WorkspaceName);
    }

    [Fact]
    public void SetLockDate_updates_lock_date()
    {
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());

        organization.SetLockDate(new DateOnly(2026, 6, 30));

        Assert.Equal(new DateOnly(2026, 6, 30), organization.LockDate);
    }
}
