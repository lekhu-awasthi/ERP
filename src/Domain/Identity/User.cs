namespace ErpApp.Domain.Identity;

/// <summary>
/// Aggregate root for the Identity bounded context. Tenant-agnostic — a User exists
/// independently of any Organization (architecture-spec.md §2/§4.1); OrganizationMembership
/// (Phase 1b) is what links a User to one or more Organizations.
/// </summary>
public sealed class User
{
    public Guid Id { get; private set; }
    public string FullName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string Phone { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    private User()
    {
    }

    public static User Register(string fullName, string email, string phone, string passwordHash)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            Email = email.Trim().ToLowerInvariant(),
            Phone = phone,
            PasswordHash = passwordHash,
            Status = UserStatus.EmailUnverified,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void MarkEmailVerified()
    {
        if (Status == UserStatus.EmailUnverified)
        {
            Status = UserStatus.Active;
        }
    }

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;
}
