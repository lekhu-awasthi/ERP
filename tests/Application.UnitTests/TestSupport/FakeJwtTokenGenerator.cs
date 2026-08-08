using ErpApp.Application.Common.Security;

namespace ErpApp.Application.UnitTests.TestSupport;

public sealed class FakeJwtTokenGenerator : IJwtTokenGenerator
{
    public JwtToken GenerateToken(Guid userId, string email) => new("fake-token", DateTimeOffset.UtcNow.AddHours(1));
}
