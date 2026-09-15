using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.DeleteSecondaryUnit;

/// <summary>
/// Phase 45 -- the delete half of the reference product's per-row <i>Action</i> column on a
/// product's Secondary Unit table (read live 2026-09-15).
///
/// <para><b>A hard delete, and that is a decision.</b> A <c>ProductSecondaryUnit</c> is priced
/// catalog metadata that no document line, stock layer or GL row points at -- every quantity in this
/// codebase is stored in the product's primary unit, and nothing persists "this line was entered in
/// Boxes". So there is no history to strand, which is exactly what makes this different from
/// <c>DeleteProductVariantCommand</c>, whose whole body is a check that nothing references the
/// row.</para>
/// </summary>
public sealed record DeleteSecondaryUnitCommand(Guid OrganizationId, Guid ProductId, Guid SecondaryUnitId)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductManage;
}
