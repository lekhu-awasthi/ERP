using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.UpdateWarehouseTransfer;

public sealed record UpdateWarehouseTransferCommand(
    Guid OrganizationId,
    Guid Id,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    DateOnly Date,
    string? Reference,
    IReadOnlyList<WarehouseTransferLineInput> Lines)
    : IRequest<UpdateWarehouseTransferResult>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILockDateSensitive, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.WarehouseTransferEdit;

    // Phase 20f (FR-2.6): moving stock between warehouses needs both entitlements -- the
    // inventory tracking that gives the movement meaning, and more than one warehouse to
    // move it between. The only requests in this codebase requiring two features.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory, TenantFeature.MultipleWarehouses];
    public DocumentType AuditDocumentType => DocumentType.WarehouseTransfer;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateWarehouseTransferResult(Guid Id, string Code, WarehouseTransferStatus Status);
