using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Commands.CreateProductionJournal;

/// <summary>
/// Raw-material lines carry a Quantity and no rate, deliberately: the cost is resolved at Approve
/// from the FIFO layers actually walked. See ProductionJournalRawMaterialLine.
/// </summary>
public sealed record CreateProductionJournalCommand(
    Guid OrganizationId,
    DateOnly Date,
    string? Reference,
    Guid ProductId,
    decimal OutputQuantity,
    Guid WarehouseId,
    Guid? BillOfMaterialsId,
    string? Notes,
    DocumentType? ReferrerType,
    Guid? ReferrerId,
    IReadOnlyList<ProductionRawMaterialLineInput> RawMaterials,
    IReadOnlyList<ProductionByProductLineInput> ByProducts,
    IReadOnlyList<ProductionExpenseLineInput> Expenses)
    : IRequest<CreateProductionJournalResult>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILockDateSensitive, ILocationBearingCommand
{
    public string PermissionKey => PermissionKeys.ProductionJournalCreate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];

    /// <summary>Phase 32 (FR-2.3/FR-3.3). The billing location this document is raised from. Out of
    /// scope under the default LocationScopeMode (SalesTransactionsOnly) and carried anyway, because
    /// an Admin can widen the scope to AllTransactions at any moment -- see
    /// <see cref="ILocationBearingCommand"/> and <see cref="LocationResolver"/>.</summary>
    public Guid? LocationId { get; init; }
}

public sealed record CreateProductionJournalResult(Guid Id, string Code, ProductionJournalStatus Status);
