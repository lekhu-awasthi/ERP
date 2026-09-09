using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Commands.UpdateProductionOrder;

public sealed record UpdateProductionOrderCommand(
    Guid OrganizationId,
    Guid Id,
    DateOnly Date,
    string? Reference,
    Guid ProductId,
    decimal OutputQuantity,
    Guid? BillOfMaterialsId,
    string? Notes,
    IReadOnlyList<ProductionRawMaterialLineInput> RawMaterials,
    IReadOnlyList<ProductionByProductLineInput> ByProducts,
    IReadOnlyList<ProductionExpenseLineInput> Expenses)
    : IRequest<UpdateProductionOrderResult>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILockDateSensitive, ILocationBearingCommand, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.ProductionOrderEdit;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => Id;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];

    /// <summary>Phase 32 (FR-2.3/FR-3.3). The billing location this document is raised from. Out of
    /// scope under the default LocationScopeMode (SalesTransactionsOnly) and carried anyway, because
    /// an Admin can widen the scope to AllTransactions at any moment -- see
    /// <see cref="ILocationBearingCommand"/> and <see cref="LocationResolver"/>.</summary>
    public Guid? LocationId { get; init; }
}

public sealed record UpdateProductionOrderResult(Guid Id, string Code, ProductionOrderStatus Status);
