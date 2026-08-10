namespace ErpApp.Domain.Tenancy;

/// <summary>
/// `RolePermission { RoleId, PermissionKey, IsGranted }` per architecture-spec.md §3.7 --
/// PermissionKey is a stable string (see Application.Common.Security.PermissionKeys), evaluated
/// by AuthorizationBehavior. No scope/module/documentType/action decomposition yet (the full
/// (scope, module, documentType, action) matrix is later work); PermissionKey is just a flat
/// string for now, enough to unblock every command from Phase 2 onward having somewhere to
/// check a permission.
/// </summary>
public sealed class RolePermission
{
    public Guid Id { get; private set; }
    public Guid RoleId { get; private set; }
    public string PermissionKey { get; private set; } = null!;
    public bool IsGranted { get; private set; }

    private RolePermission()
    {
    }

    /// <summary>
    /// Constructs a row with an explicit, caller-supplied Id -- see Role.Create's doc comment
    /// for why HasData/tests need a stable Id rather than a freshly generated one.
    /// </summary>
    public static RolePermission Create(Guid id, Guid roleId, string permissionKey, bool isGranted) =>
        new() { Id = id, RoleId = roleId, PermissionKey = permissionKey, IsGranted = isGranted };
}
