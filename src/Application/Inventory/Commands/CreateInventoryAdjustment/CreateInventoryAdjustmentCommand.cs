using ErpApp.Application.Common.Locations;
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
    : IRequest<CreateInventoryAdjustmentResult>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILockDateSensitive, IAuditableRequest, ILocationBearingCommand
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentCreate;

    // Phase 20f (FR-2.6): the Inventory context is only available to a tenant that opted
    // into Track Inventory. Catalog (Products/Categories/Units) is deliberately NOT gated --
    // live-confirmed that the reference product files those under Inventory in its nav but
    // every tenant needs them. See phase-20f-status.md.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
    public DocumentType AuditDocumentType => DocumentType.InventoryAdjustment;

    /// <summary>Phase 32 (FR-2.3/FR-3.3). The billing location this document is raised from. Out of
    /// scope under the default LocationScopeMode (SalesTransactionsOnly) and carried anyway, because
    /// an Admin can widen the scope to AllTransactions at any moment -- see
    /// <see cref="ILocationBearingCommand"/> and <see cref="LocationResolver"/>.</summary>
    public Guid? LocationId { get; init; }
}

public sealed record CreateInventoryAdjustmentResult(Guid Id, string Code, InventoryAdjustmentStatus Status);
