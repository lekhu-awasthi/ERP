using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.UpdateSecondaryUnit;

/// <summary>
/// Phase 45 -- the edit half of the reference product's per-row <i>Action</i> column on a product's
/// Secondary Unit table (read live 2026-09-15). Phase 3 shipped the add and nothing else, so a
/// mistyped conversion rate was permanent.
///
/// <para><b>UnitId is absent from this record, not merely ignored.</b> The unit is the row's
/// identity -- a product refuses two rows for one unit -- so changing it is a delete and an add, not
/// an edit. A present-and-ignored field reads to a client as accepted (phase-43's
/// <c>WorkspaceName</c>).</para>
///
/// <para>Rides <see cref="PermissionKeys.ProductManage"/>, the same key as the add: editing a
/// product's unit matrix is editing the product.</para>
/// </summary>
public sealed record UpdateSecondaryUnitCommand(
    Guid OrganizationId,
    Guid ProductId,
    Guid SecondaryUnitId,
    decimal ConversionRate,
    decimal SellingPrice,
    decimal PurchasePrice)
    : IRequest<SecondaryUnitResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductManage;
}

public sealed record SecondaryUnitResult(
    Guid Id, Guid ProductId, Guid UnitId, decimal ConversionRate, decimal SellingPrice, decimal PurchasePrice);
