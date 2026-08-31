using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.CreateInventoryAdjustment;

public sealed record CreateInventoryAdjustmentCommand(
    Guid OrganizationId,
    Guid WarehouseId,
    DateOnly Date,
    string? Reference,
    IReadOnlyList<InventoryAdjustmentLineInput> Lines)
    : IRequest<CreateInventoryAdjustmentResult>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILockDateSensitive, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentCreate;

    // Phase 20f (FR-2.6): the Inventory context is only available to a tenant that opted
    // into Track Inventory. Catalog (Products/Categories/Units) is deliberately NOT gated --
    // live-confirmed that the reference product files those under Inventory in its nav but
    // every tenant needs them. See phase-20f-status.md.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
    public DocumentType AuditDocumentType => DocumentType.InventoryAdjustment;
}

public sealed record CreateInventoryAdjustmentResult(Guid Id, string Code, InventoryAdjustmentStatus Status);
