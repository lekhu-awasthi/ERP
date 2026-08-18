namespace ErpApp.Domain.Tenancy;

/// <summary>
/// The permission-holder referenced by OrganizationMembership.RoleId (architecture-spec.md
/// §3.7). Phase 1c seeded exactly two <b>system-level</b> rows (Admin, Member) shared by every
/// Organization -- <see cref="OrganizationId"/> is <c>null</c> for both, kept exactly as-is so
/// every Organization still gets sensible defaults with zero migration risk to existing data.
/// Phase 14 (Role Reference) adds real per-tenant custom roles on top: <see cref="OrganizationId"/>
/// non-null for a role created through <see cref="CreateCustom"/>, scoped to and only visible/
/// editable within that one Organization. The two system rows are deliberately **not** editable
/// through Phase 14's Role Reference UI -- their RolePermission rows are shared globally, so
/// mutating "Member" for one Organization would silently change it for every tenant; only a
/// custom (non-null-OrganizationId) role's permissions can be edited (see
/// UpdateRolePermissionsCommandHandler's own doc comment).
/// </summary>
public sealed class Role
{
    /// <summary>Well-known system role: every permission granted (see RoleConfiguration's seed data).</summary>
    public static readonly Guid AdminId = Guid.Parse("00000000-0000-0000-0001-000000000001");

    /// <summary>Well-known system role: read-only, no permissions granted by default.</summary>
    public static readonly Guid MemberId = Guid.Parse("00000000-0000-0000-0001-000000000002");

    public Guid Id { get; private set; }

    /// <summary><c>null</c> for a shared system role (Admin/Member); set for a tenant's own custom role.</summary>
    public Guid? OrganizationId { get; private set; }

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
    /// <paramref name="organizationId"/> defaults to null (a system role) since every existing
    /// call site seeds one of the two shared system rows.
    /// </summary>
    public static Role Create(Guid id, string name, string? description = null, Guid? organizationId = null) =>
        new() { Id = id, Name = name, Description = description, OrganizationId = organizationId };

    /// <summary>A tenant's own custom role (Phase 14), created through CreateRoleCommand.</summary>
    public static Role CreateCustom(Guid organizationId, string name, string? description = null) =>
        new() { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = name, Description = description };

    public void Update(string name, string? description)
    {
        Name = name;
        Description = description;
    }

    /// <summary>Maps the API/UI-facing role selector (MembershipRole) to its well-known RoleId.</summary>
    public static Guid ResolveId(MembershipRole role) => role switch
    {
        MembershipRole.Admin => AdminId,
        MembershipRole.Member => MemberId,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };
}
