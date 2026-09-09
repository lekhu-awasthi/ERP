using ErpApp.Application.Common.Locations;
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
    : IRequest<UpdateWarehouseTransferResult>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILockDateSensitive, IAuditableRequestWithId, ILocationBearingCommand, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.WarehouseTransferEdit;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => Id;

    // Phase 20f (FR-2.6): moving stock between warehouses needs both entitlements -- the
    // inventory tracking that gives the movement meaning, and more than one warehouse to
    // move it between. The only requests in this codebase requiring two features.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory, TenantFeature.MultipleWarehouses];
    public DocumentType AuditDocumentType => DocumentType.WarehouseTransfer;

    /// <summary>Phase 32 (FR-2.3/FR-3.3). The billing location this document is raised from. Out of
    /// scope under the default LocationScopeMode (SalesTransactionsOnly) and carried anyway, because
    /// an Admin can widen the scope to AllTransactions at any moment -- see
    /// <see cref="ILocationBearingCommand"/> and <see cref="LocationResolver"/>.</summary>
    public Guid? LocationId { get; init; }
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateWarehouseTransferResult(Guid Id, string Code, WarehouseTransferStatus Status);
