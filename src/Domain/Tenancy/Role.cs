namespace ErpApp.Domain.Tenancy;

/// <summary>
/// The permission-holder referenced by OrganizationMembership.RoleId (architecture-spec.md
/// §3.7). Phase 1c seeds exactly two <b>system-level</b> rows (Admin, Member) shared by every
/// Organization rather than per-org rows -- there's nothing yet to customize per-tenant (no Role
/// Reference editor), so a shared catalog keeps CreateOrganizationCommand's seeding to "look up
/// a well-known RoleId" instead of creating+granting two new rows on every Organization. The
/// real per-document-type permission matrix (and, if ever needed, per-org custom roles) is
/// later work (roadmap Phase 8+ "Role Reference full editor").
/// </summary>
public sealed class Role
{
    /// <summary>Well-known system role: every permission granted (see RoleConfiguration's seed data).</summary>
    public static readonly Guid AdminId = Guid.Parse("00000000-0000-0000-0001-000000000001");

    /// <summary>Well-known system role: read-only, no permissions granted by default.</summary>
    public static readonly Guid MemberId = Guid.Parse("00000000-0000-0000-0001-000000000002");

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    private Role()
    {
    }

    /// <summary>
    /// Constructs a Role row with an explicit, caller-supplied Id -- used by RoleConfiguration's
    /// HasData seed (which needs a stable Id across migrations, not a fresh Guid every time
    /// OnModelCreating runs) and by tests that need a matching Role row in an InMemory context
    /// (TestAppDbContext doesn't apply EF configurations/HasData -- see its doc comment).
    /// </summary>
    public static Role Create(Guid id, string name, string? description = null) =>
        new() { Id = id, Name = name, Description = description };

    /// <summary>Maps the API/UI-facing role selector (MembershipRole) to its well-known RoleId.</summary>
    public static Guid ResolveId(MembershipRole role) => role switch
    {
        MembershipRole.Admin => AdminId,
        MembershipRole.Member => MemberId,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };
}
