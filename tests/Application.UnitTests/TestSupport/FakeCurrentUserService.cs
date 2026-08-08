using ErpApp.Application.Common.Security;

namespace ErpApp.Application.UnitTests.TestSupport;

public sealed class FakeCurrentUserService(Guid userId) : ICurrentUserService
{
    public Guid UserId { get; } = userId;
}
