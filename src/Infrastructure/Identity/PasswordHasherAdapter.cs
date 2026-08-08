using ErpApp.Application.Common.Security;
using ErpApp.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace ErpApp.Infrastructure.Identity;

/// <summary>Wraps ASP.NET Core Identity's PasswordHasher&lt;TUser&gt; without pulling in full Identity/UserManager.</summary>
public sealed class PasswordHasherAdapter : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(user: null!, password);

    public bool Verify(string passwordHash, string providedPassword) =>
        _inner.VerifyHashedPassword(user: null!, passwordHash, providedPassword) != PasswordVerificationResult.Failed;
}
