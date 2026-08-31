using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.VoidInventoryAdjustment;

public sealed record VoidInventoryAdjustmentCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidInventoryAdjustmentResult>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentVoid;

    // Phase 20f (FR-2.6): the Inventory context is only available to a tenant that opted
    // into Track Inventory. Catalog (Products/Categories/Units) is deliberately NOT gated --
    // live-confirmed that the reference product files those under Inventory in its nav but
    // every tenant needs them. See phase-20f-status.md.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
    public DocumentType LockDateDocumentType => DocumentType.InventoryAdjustment;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidInventoryAdjustmentResult(
    Guid Id, string Code, InventoryAdjustmentStatus Status, DateTimeOffset? VoidedAt);
