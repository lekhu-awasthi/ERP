using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.GetWarehouseTransfer;

public sealed record GetWarehouseTransferQuery(Guid OrganizationId, Guid Id)
    : IRequest<WarehouseTransferDetailDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.WarehouseTransferView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => Id;

    // Phase 20f (FR-2.6): moving stock between warehouses needs both entitlements -- the
    // inventory tracking that gives the movement meaning, and more than one warehouse to
    // move it between. The only requests in this codebase requiring two features.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory, TenantFeature.MultipleWarehouses];
}

/// <param name="UnitId">Phase 52 -- the unit this line was entered in, null when it was the
/// product's own primary unit. Present so a form can round-trip the user's choice: phase 35a's
/// rule is that adding a field to many aggregates owes write, read and every prefill between,
/// and a detail DTO that drops it leaves the form unable to show what was saved.</param>
/// <param name="UnitName">The unit's short name (<c>BTL</c>), for rendering beside the quantity.</param>
/// <param name="ConversionFactor">How many primary units one of them was worth <b>when this line
/// was written</b>. Sent so a reader can see the frozen fact rather than infer it from a
/// catalogue that may since have changed. The converted quantity is deliberately not sent --
/// it is <c>Quantity x ConversionFactor</c>, and shipping it would put a second quantity on the
/// wire that can contradict the first.</param>
public sealed record WarehouseTransferLineDto(
    Guid Id, Guid ProductId, decimal Quantity,
    Guid? UnitId, string? UnitName, decimal ConversionFactor);

public sealed record WarehouseTransferDetailDto(
    Guid Id,
    Guid OrganizationId,
    string Code,
    DateOnly Date,
    string? Reference,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    WarehouseTransferStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<WarehouseTransferLineDto> Lines,
    // Phase 35 -- the billing location this document was raised from, null when the tenant's
    // LocationScopeMode excludes this type. Phase 32 put the column on all 17 types but the header
    // picker on Invoice alone, so only InvoiceDetailDto ever carried it back: a detail query
    // projecting a DTO silently drops a field the aggregate has, and the form would then post the
    // picker's default over a stored location on every edit. Fourteen instances of phase-32's own
    // carried gotcha.
    Guid? LocationId);
