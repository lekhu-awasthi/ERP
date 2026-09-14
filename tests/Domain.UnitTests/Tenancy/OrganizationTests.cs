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

    /// <summary>
    /// Phase 43 (39 carried item #2) -- the eight details a tenant can correct after the wizard
    /// closed. Trimmed on the way in, matching Create.
    /// </summary>
    [Fact]
    public void UpdateDetails_replaces_every_editable_field()
    {
        var organization = Organization.Create(
            "Acme Traders", "Retail", "Old address", new DateOnly(2026, 1, 1), true,
            "acme-traders", "old@example.com", "01-1111111", "111111111", "old.example.com",
            Guid.NewGuid());

        organization.UpdateDetails(
            "  Acme Traders Pvt Ltd  ", "  Wholesale  ", "New address", new DateOnly(2026, 4, 14), false,
            "new@example.com", "01-2222222", "222222222", "new.example.com");

        Assert.Equal("Acme Traders Pvt Ltd", organization.Name);
        Assert.Equal("Wholesale", organization.Industry);
        Assert.Equal("New address", organization.Address);
        Assert.Equal(new DateOnly(2026, 4, 14), organization.AccountingStartDate);
        Assert.False(organization.IsVatRegistered);
        Assert.Equal("new@example.com", organization.Email);
        Assert.Equal("01-2222222", organization.Phone);
        Assert.Equal("222222222", organization.PanNumber);
        Assert.Equal("new.example.com", organization.Website);
    }

    /// <summary>
    /// The three fields this command deliberately cannot reach. Asserted rather than merely
    /// documented: the failure mode of "we left it out" is a later phase adding it to the
    /// parameter list because nothing said not to.
    /// </summary>
    [Fact]
    public void UpdateDetails_leaves_the_workspace_name_the_lock_date_and_the_logo_alone()
    {
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        organization.SetLockDate(new DateOnly(2026, 6, 30));
        organization.SetLogo("tenant/logo.png", "image/png");

        organization.UpdateDetails(
            "Renamed", "Wholesale", null, new DateOnly(2026, 1, 1), true, null, null, null, null);

        Assert.Equal("acme-traders", organization.WorkspaceName);
        Assert.Equal(new DateOnly(2026, 6, 30), organization.LockDate);
        Assert.Equal("tenant/logo.png", organization.LogoStorageKey);
        Assert.Equal("image/png", organization.LogoContentType);
    }

    [Theory]
    [InlineData("", "Retail")]
    [InlineData("   ", "Retail")]
    [InlineData("Acme", "")]
    public void UpdateDetails_refuses_a_blank_name_or_industry(string name, string industry)
    {
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());

        Assert.ThrowsAny<ArgumentException>(() => organization.UpdateDetails(
            name, industry, null, new DateOnly(2026, 1, 1), true, null, null, null, null));
    }

    [Fact]
    public void UpdateDetails_refuses_a_missing_accounting_start_date()
    {
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => organization.UpdateDetails(
            "Acme", "Retail", null, default, true, null, null, null, null));
    }
}
