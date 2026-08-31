using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.UpdateInventoryAdjustment;

public sealed record UpdateInventoryAdjustmentCommand(
    Guid OrganizationId,
    Guid Id,
    Guid WarehouseId,
    DateOnly Date,
    string? Reference,
    IReadOnlyList<InventoryAdjustmentLineInput> Lines)
    : IRequest<UpdateInventoryAdjustmentResult>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILockDateSensitive, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentEdit;

    // Phase 20f (FR-2.6): the Inventory context is only available to a tenant that opted
    // into Track Inventory. Catalog (Products/Categories/Units) is deliberately NOT gated --
    // live-confirmed that the reference product files those under Inventory in its nav but
    // every tenant needs them. See phase-20f-status.md.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
    public DocumentType AuditDocumentType => DocumentType.InventoryAdjustment;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateInventoryAdjustmentResult(Guid Id, string Code, InventoryAdjustmentStatus Status);
