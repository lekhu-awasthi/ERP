using ErpApp.Application.Common.Security;

namespace ErpApp.Application.UnitTests.TestSupport;

public sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string passwordHash, string providedPassword) => passwordHash == Hash(providedPassword);
}
