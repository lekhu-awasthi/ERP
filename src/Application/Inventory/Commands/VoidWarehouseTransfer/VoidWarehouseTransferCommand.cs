using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.VoidWarehouseTransfer;

public sealed record VoidWarehouseTransferCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidWarehouseTransferResult>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.WarehouseTransferVoid;

    // Phase 20f (FR-2.6): moving stock between warehouses needs both entitlements -- the
    // inventory tracking that gives the movement meaning, and more than one warehouse to
    // move it between. The only requests in this codebase requiring two features.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory, TenantFeature.MultipleWarehouses];
    public DocumentType LockDateDocumentType => DocumentType.WarehouseTransfer;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidWarehouseTransferResult(Guid Id, string Code, WarehouseTransferStatus Status, DateTimeOffset? VoidedAt);
