using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Tenancy;

public class RoleTests
{
    [Fact]
    public void ResolveId_maps_admin_and_member_to_their_well_known_ids()
    {
        Assert.Equal(Role.AdminId, Role.ResolveId(MembershipRole.Admin));
        Assert.Equal(Role.MemberId, Role.ResolveId(MembershipRole.Member));
    }
}
